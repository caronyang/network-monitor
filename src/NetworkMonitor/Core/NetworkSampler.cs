using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NetworkMonitor.Core;

public sealed class IfaceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public long RxRate { get; init; }
    public long TxRate { get; init; }
    public long SessionRx { get; init; }
    public long SessionTx { get; init; }
    public long Speed { get; init; }
    public bool Connected { get; init; }
    public bool Loopback { get; init; }
    public NetworkInterfaceType Type { get; init; }
}

public sealed class ProcInfo
{
    public required string Name { get; init; }        // canonical lowercase base name
    public required string Key { get; init; }         // traffic-table key ("P:name")
    public long RxRate { get; init; }
    public long TxRate { get; init; }
    public long SessionRx { get; init; }
    public long SessionTx { get; init; }
    public bool Running { get; init; }
}

public sealed class Snapshot
{
    public static readonly Snapshot Empty = new()
    {
        Time = DateTime.MinValue,
        Items = Array.Empty<IfaceInfo>(),
        Processes = Array.Empty<ProcInfo>(),
        Valid = false,
    };

    public DateTime Time { get; init; }
    public IReadOnlyList<IfaceInfo> Items { get; init; } = Array.Empty<IfaceInfo>();
    public IReadOnlyList<ProcInfo> Processes { get; init; } = Array.Empty<ProcInfo>();
    public ProcessMonitorState ProcState { get; init; } = ProcessMonitorState.Stopped;
    public long DownRate { get; init; }
    public long UpRate { get; init; }
    public bool Valid { get; init; }
}

/// <summary>
/// Samples every network interface on a dedicated low-priority thread, converts the
/// byte counters into rates, aggregates deltas into local-minute buckets and writes them
/// to SQLite once per minute (one small transaction per flush).
/// Counters come from a single GetIfTable2 call per tick (~1 ms), so the sampler costs
/// roughly 0.1% of one core at a 1 second interval.
/// </summary>
public sealed class NetworkSampler : IDisposable
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);

    private readonly AppSettings _settings;
    private readonly TrafficDatabase _db;
    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private Snapshot _latest = Snapshot.Empty;

    private readonly Dictionary<string, long> _baseRx = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _baseTx = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _sessionRx = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _sessionTx = new(StringComparer.Ordinal);

    // per monitored process: ETW cumulative baseline + session totals (keyed by canonical name)
    private readonly Dictionary<string, (long Rx, long Tx)> _procBase = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Rx, long Tx)> _procSession = new(StringComparer.OrdinalIgnoreCase);

    // traffic stopwatch ("碼表")
    private volatile bool _measActive;
    private DateTime _measStart;
    private long _measRx;
    private long _measTx;

    // (interface id, local minute timestamp) -> accumulated deltas
    private readonly Dictionary<(string, long), TrafficRow> _pending = new();
    private long _bucketTs;
    private long _lastTickMs;
    private long _lastPruneMs;
    private long _samples;
    private bool _hasTick;
    private volatile bool _resetBaseline;
    private Stopwatch? _clock;

    public NetworkSampler(AppSettings settings, TrafficDatabase db)
    {
        _settings = settings;
        _db = db;
    }

    public Snapshot Latest => Volatile.Read(ref _latest);

    public long SampleCount => Interlocked.Read(ref _samples);

    /// <summary>ETW per-process byte source. Assigned once at startup; may be null on failure paths.</summary>
    public ProcessTrafficMonitor? Processes { get; set; }

    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "NetworkSampler",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start(_cts.Token);
    }

    private void Run(object? state)
    {
        if (state is not CancellationToken ct) return;

        var stopwatch = Stopwatch.StartNew();
        _clock = stopwatch;
        long nextDue = 0;
        _lastTickMs = 0;
        PruneNow();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                long now = stopwatch.ElapsedMilliseconds;
                if (now < nextDue)
                {
                    if (ct.WaitHandle.WaitOne((int)Math.Min(int.MaxValue, nextDue - now))) break;
                    continue;
                }

                nextDue = now + IntervalMs;
                try
                {
                    Sample(now);
                }
                catch
                {
                    // a transient network-stack error must not kill the sampler
                }

                if (now - _lastPruneMs >= PruneInterval.TotalMilliseconds) PruneNow();
            }
        }
        finally
        {
            Flush();
        }
    }

    private int IntervalMs => Math.Clamp(_settings.SampleIntervalMs, 200, 60_000);

    private void Sample(long nowMs)
    {
        if (_resetBaseline)
        {
            // a filter setting just changed: the dropped adapters have stale counters and
            // would otherwise be reported as one huge delta when they come back
            _resetBaseline = false;
            _baseRx.Clear();
            _baseTx.Clear();
        }

        var now = DateTime.Now;
        long bucketTs = DateUtil.MinuteTimestamp(now);
        int dayKey = DateUtil.DayKey(now);
        int hour = now.Hour;

        // Close the previous minute before recording anything new.
        if (_bucketTs != 0 && bucketTs != _bucketTs) Flush();
        _bucketTs = bucketTs;

        double elapsedSec = _hasTick ? Math.Max(0.001, (nowMs - _lastTickMs) / 1000.0) : 0;
        _lastTickMs = nowMs;
        _hasTick = true;

        var rows = IfTable.Read();
        if (rows.Count == 0) return;

        var items = new List<IfaceInfo>(rows.Count);
        long downTotal = 0, upTotal = 0;

        foreach (var row in rows)
        {
            bool loopback = row.Type == IfTableRow.IfTypeLoopback;
            if (loopback && !_settings.IncludeLoopback) continue;

            // Packet filter / callout adapters (WFP, Npcap, QoS, AV drivers, ...) mirror the
            // traffic of their parent adapter - recording them would count the same bytes twice.
            // The RAS/VPN stack ("Local Area Connection* N" / "區域連線* N") is Up all day and
            // never carries real traffic, so it is noise rather than an adapter worth showing.
            if (!_settings.ShowFilterAdapters)
            {
                if (row.Software) continue;
                if (IsWindowsAutoName(row.Name)) continue;
            }

            string id = row.Id;
            long rx = (long)row.Rx, tx = (long)row.Tx;
            long dRx = 0, dTx = 0;

            if (_baseRx.TryGetValue(id, out var prevRx))
            {
                dRx = rx - prevRx;
                if (dRx < 0) dRx = 0; // counter reset (adapter re-initialised)
            }

            if (_baseTx.TryGetValue(id, out var prevTx))
            {
                dTx = tx - prevTx;
                if (dTx < 0) dTx = 0;
            }

            _baseRx[id] = rx;
            _baseTx[id] = tx;
            _sessionRx[id] = _sessionRx.GetValueOrDefault(id) + dRx;
            _sessionTx[id] = _sessionTx.GetValueOrDefault(id) + dTx;

            if (dRx > 0 || dTx > 0)
            {
                var key = (id, bucketTs);
                if (_pending.TryGetValue(key, out var pending))
                    _pending[key] = pending with { Rx = pending.Rx + dRx, Tx = pending.Tx + dTx };
                else
                    _pending[key] = new TrafficRow(id, bucketTs, dayKey, hour, dRx, dTx);
            }

            bool connected = row.Connected;
            if (!connected && !_settings.IncludeDisconnected) continue;

            long rateRx = elapsedSec > 0 ? (long)Math.Round(dRx / elapsedSec) : 0;
            long rateTx = elapsedSec > 0 ? (long)Math.Round(dTx / elapsedSec) : 0;
            downTotal += rateRx;
            upTotal += rateTx;

            items.Add(new IfaceInfo
            {
                Id = id,
                Name = row.Name,
                Description = row.Description,
                RxRate = rateRx,
                TxRate = rateTx,
                SessionRx = _sessionRx.GetValueOrDefault(id),
                SessionTx = _sessionTx.GetValueOrDefault(id),
                Speed = row.LinkSpeed,
                Connected = connected,
                Loopback = loopback,
                Type = (NetworkInterfaceType)row.Type,
            });

            if (_measActive)
            {
                Interlocked.Add(ref _measRx, dRx);
                Interlocked.Add(ref _measTx, dTx);
            }
        }

        items.Sort(CompareInterfaces);

        var procs = SampleProcesses(elapsedSec, bucketTs, dayKey, hour);

        Interlocked.Increment(ref _samples);
        Volatile.Write(ref _latest, new Snapshot
        {
            Time = now,
            Items = items,
            Processes = procs,
            ProcState = Processes?.State ?? ProcessMonitorState.Stopped,
            DownRate = downTotal,
            UpRate = upTotal,
            Valid = true,
        });
    }

    /// <summary>
    /// Reads the ETW per-process counters, converts them to rates and minute buckets, and
    /// keeps one <see cref="ProcInfo"/> per monitored process. Rows are produced even when
    /// the monitor cannot run (no elevation) so the UI still lists the selected processes.
    /// </summary>
    private IReadOnlyList<ProcInfo> SampleProcesses(double elapsedSec, long bucketTs, int dayKey, int hour)
    {
        var names = _settings.ProcessMonitors;
        if (names.Count == 0)
        {
            if (Processes is { State: ProcessMonitorState.Running }) Processes.Stop();
            _procBase.Clear();
            _procSession.Clear();
            return Array.Empty<ProcInfo>();
        }

        Processes?.EnsureRunning();
        bool etw = Processes?.State == ProcessMonitorState.Running;
        if (etw) Processes!.TrimCaches();

        // forget processes that were removed from the monitor list
        if (_procBase.Count > 0)
        {
            List<string>? stale = null;
            foreach (var key in _procBase.Keys)
                if (!names.Contains(key)) (stale ??= new List<string>()).Add(key);
            if (stale is not null)
                foreach (var key in stale) { _procBase.Remove(key); _procSession.Remove(key); }
        }

        var list = new List<ProcInfo>(names.Count);
        foreach (var name in names)
        {
            long dRx = 0, dTx = 0;
            if (etw)
            {
                var (rx, tx) = Processes!.CumulativeFor(name);
                if (_procBase.TryGetValue(name, out var b))
                {
                    dRx = Math.Max(0, rx - b.Rx);
                    dTx = Math.Max(0, tx - b.Tx);
                }

                _procBase[name] = (rx, tx);
            }
            else
            {
                // baseline restarts the moment a session comes up
                _procBase.Remove(name);
            }

            var sess = _procSession.GetValueOrDefault(name);
            sess.Rx += dRx;
            sess.Tx += dTx;
            _procSession[name] = sess;

            if (dRx > 0 || dTx > 0)
            {
                string key = TrafficKeys.ForProcess(name);
                var bucketKey = (key, bucketTs);
                if (_pending.TryGetValue(bucketKey, out var pending))
                    _pending[bucketKey] = pending with { Rx = pending.Rx + dRx, Tx = pending.Tx + dTx };
                else
                    _pending[bucketKey] = new TrafficRow(key, bucketTs, dayKey, hour, dRx, dTx);
            }

            bool alive;
            try { alive = Process.GetProcessesByName(name).Length > 0; }
            catch { alive = false; }

            list.Add(new ProcInfo
            {
                Name = name,
                Key = TrafficKeys.ForProcess(name),
                RxRate = elapsedSec > 0 ? (long)Math.Round(dRx / elapsedSec) : 0,
                TxRate = elapsedSec > 0 ? (long)Math.Round(dTx / elapsedSec) : 0,
                SessionRx = sess.Rx,
                SessionTx = sess.Tx,
                Running = alive,
            });
        }

        return list;
    }

    private static int CompareInterfaces(IfaceInfo a, IfaceInfo b)
    {
        int ta = TypeOrder(a), tb = TypeOrder(b);
        if (ta != tb) return ta.CompareTo(tb);
        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int TypeOrder(IfaceInfo info)
    {
        if (info.Loopback) return 9;
        return info.Type switch
        {
            NetworkInterfaceType.Ethernet => 0,
            NetworkInterfaceType.GigabitEthernet => 0,
            NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => 0,
            NetworkInterfaceType.Wireless80211 => 1,
            NetworkInterfaceType.Ppp => 2,
            NetworkInterfaceType.Tunnel => 4,
            NetworkInterfaceType.Loopback => 9,
            _ => 3,
        };
    }

    private void Flush()
    {
        if (_pending.Count == 0) return;
        var rows = new List<TrafficRow>(_pending.Count);
        foreach (var row in _pending.Values) rows.Add(row);
        _pending.Clear();
        try
        {
            _db.WriteBuckets(rows);
        }
        catch
        {
            // disk full / db locked: keep the app alive, data loss limited to one minute
        }
    }

    public void PruneNow()
    {
        _lastPruneMs = _clock?.ElapsedMilliseconds ?? 0;
        try
        {
            _db.Prune(_settings.RetentionDays);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Drops the rate baselines on the next tick (call after a filter setting changed).</summary>
    public void ResetBaseline() => _resetBaseline = true;

    // ---------------------------------------------------------- traffic stopwatch

    /// <summary>Raised on the sampler thread after a measurement has been persisted.</summary>
    public event Action? MeasurementCompleted;

    public bool Measuring => _measActive;

    /// <summary>Local start time of the running measurement (DateTime.MinValue when idle).</summary>
    public DateTime MeasurementStart => _measStart;

    public long MeasurementRx => Volatile.Read(ref _measRx);

    public long MeasurementTx => Volatile.Read(ref _measTx);

    /// <summary>Starts accumulating total traffic (the "碼表"). No-op while already running.</summary>
    public void StartMeasurement()
    {
        if (_measActive) return;
        Interlocked.Exchange(ref _measRx, 0);
        Interlocked.Exchange(ref _measTx, 0);
        _measStart = DateTime.Now;
        _measActive = true;
    }

    /// <summary>Stops the measurement and records it to the database.</summary>
    public void StopMeasurement()
    {
        if (!_measActive) return;
        _measActive = false;
        var start = _measStart;
        var end = DateTime.Now;
        long rx = Interlocked.Read(ref _measRx);
        long tx = Interlocked.Read(ref _measTx);

        try
        {
            _db.WriteMeasurement(new Measurement(
                new DateTimeOffset(start).ToUnixTimeSeconds(),
                new DateTimeOffset(end).ToUnixTimeSeconds(),
                rx, tx));
        }
        catch
        {
            // a db error must not lose the state reset; the record is simply dropped
        }

        try { MeasurementCompleted?.Invoke(); } catch { /* handler errors are not ours */ }
    }

    /// <summary>
    /// True for names Windows invents for adapters that do not have one, currently the
    /// WAN miniports of the RAS/VPN stack ("Local Area Connection* 6", "區域連線* 6").
    /// They are permanently Up and carry no real traffic.
    /// </summary>
    private static bool IsWindowsAutoName(string name) =>
        name.StartsWith("Local Area Connection*", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("區域連線*", StringComparison.Ordinal);

    public void Dispose()
    {
        var thread = _thread;
        _thread = null;
        if (thread is not null)
        {
            _cts?.Cancel();
            thread.Join(5000);
        }

        Flush();
        _cts?.Dispose();
        _cts = null;
    }
}
