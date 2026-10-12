using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetworkMonitor.Core;

public enum ProcessMonitorState
{
    /// <summary>No ETW session (nothing to monitor or stopped on purpose).</summary>
    Stopped,
    /// <summary>Session active, counting bytes.</summary>
    Running,
    /// <summary>Start failed: the process is not elevated (kernel ETW needs admin).</summary>
    RequiresAdmin,
    /// <summary>Start failed for another reason.</summary>
    Failed,
}

/// <summary>
/// Cumulative per-process byte counters fed by the Microsoft-Windows-Kernel-Network
/// ETW provider (TCP + UDP, IPv4 + IPv6, every send/receive that hits the transport
/// layer). A dedicated thread runs the event loop; counters are plain Interlocked adds,
/// so packet events cost a few nanoseconds and nothing is polled.
/// Requires elevation: without it the state degrades to <see cref="ProcessMonitorState.RequiresAdmin"/>.
/// </summary>
public sealed class ProcessTrafficMonitor : IDisposable
{
    private sealed class Counters
    {
        public long Rx;
        public long Tx;
        public long LastMs;
    }

    private readonly ConcurrentDictionary<int, Counters> _counters = new();
    private readonly ConcurrentDictionary<int, string> _nameByPid = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TraceEventSession? _session;
    private ETWTraceEventSource? _source;
    private Thread? _thread;
    private volatile bool _stopping;
    private long _lastTrimMs;

    public ProcessMonitorState State { get; private set; } = ProcessMonitorState.Stopped;
    public string? LastError { get; private set; }

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Starts the session if it is not running. Cheap; safe to call every tick.</summary>
    public void EnsureRunning()
    {
        lock (_counters) // serialises start/stop; the counters dictionary is only a handy lock object
        {
            if (State == ProcessMonitorState.Running) return;
            if (!IsElevated)
            {
                State = ProcessMonitorState.RequiresAdmin;
                return;
            }

            try
            {
                StartCore();
                LastError = null;
                State = ProcessMonitorState.Running;
                Diag.Log("etw: kernel-network session running");
            }
            catch (UnauthorizedAccessException ex)
            {
                Cleanup();
                State = ProcessMonitorState.RequiresAdmin;
                Diag.Log($"etw: access denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                Cleanup();
                LastError = ex.Message;
                State = ProcessMonitorState.Failed;
                Diag.Log($"etw: failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_counters)
        {
            Cleanup();
            _counters.Clear();
            _nameByPid.Clear();
            State = ProcessMonitorState.Stopped;
        }
    }

    /// <summary>
    /// Cumulative bytes received/sent by every process whose image base name matches
    /// <paramref name="name"/> (lowercase, without extension).
    /// </summary>
    public (long Rx, long Tx) CumulativeFor(string name)
    {
        long rx = 0, tx = 0;
        foreach (var kv in _counters)
        {
            if (NameOf(kv.Key) != name) continue;
            var c = kv.Value;
            rx += Volatile.Read(ref c.Rx);
            tx += Volatile.Read(ref c.Tx);
        }

        return (rx, tx);
    }

    /// <summary>Aggregated totals by process name (for diagnostics/self-tests).</summary>
    public List<(string Name, long Rx, long Tx)> TopProcesses(int top = 15)
    {
        var byName = new Dictionary<string, (long Rx, long Tx)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _counters)
        {
            string name = NameOf(kv.Key);
            if (name.Length == 0) continue;
            var c = kv.Value;
            var cur = byName.GetValueOrDefault(name);
            byName[name] = (cur.Rx + Volatile.Read(ref c.Rx), cur.Tx + Volatile.Read(ref c.Tx));
        }

        return byName.OrderByDescending(kv => kv.Value.Rx + kv.Value.Tx).Take(top)
                     .Select(kv => (kv.Key, kv.Value.Rx, kv.Value.Tx)).ToList();
    }

    /// <summary>Drops counters of processes that have been silent for a while.</summary>
    public void TrimCaches()
    {
        long now = _clock.ElapsedMilliseconds;
        if (now - _lastTrimMs < 10_000) return;
        _lastTrimMs = now;

        foreach (var kv in _counters)
        {
            if (now - Volatile.Read(ref kv.Value.LastMs) > 300_000 && !PidAlive(kv.Key))
            {
                _counters.TryRemove(kv.Key, out _);
                _nameByPid.TryRemove(kv.Key, out _);
            }
        }
    }

    // ---------------------------------------------------------------- internals

    private void StartCore()
    {
        // Unique name: a crashed instance leaves no session that could block the next start
        // (real-time kernel sessions die with their controller process anyway).
        string sessionName = $"NetworkMonitor-Kernel-{Environment.ProcessId}";
        var session = new TraceEventSession(sessionName);
        try
        {
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            var source = new ETWTraceEventSource(sessionName, TraceEventSourceType.Session);
            var kernel = source.Kernel;
            kernel.TcpIpRecv += OnTcpRecv;
            kernel.TcpIpSend += OnTcpSend;
            kernel.TcpIpRecvIPV6 += OnTcpRecvV6;
            kernel.TcpIpSendIPV6 += OnTcpSendV6;
            kernel.UdpIpRecv += OnUdpRecv;
            kernel.UdpIpSend += OnUdpSend;

            _stopping = false;
            _thread = new Thread(ProcessLoop) { IsBackground = true, Name = "EtwNetwork", Priority = ThreadPriority.Normal };

            _session = session;
            _source = source;
            _thread.Start();
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void ProcessLoop()
    {
        var source = _source;
        if (source is null) return;
        try
        {
            source.Process();
        }
        catch (Exception ex)
        {
            if (!_stopping)
            {
                LastError = ex.Message;
                State = ProcessMonitorState.Failed;
            }
        }
    }

    private void Cleanup()
    {
        _stopping = true;
        try { _session?.Stop(); } catch { /* ignore */ }
        var thread = _thread;
        _thread = null;
        if (thread is not null) thread.Join(3000);
        try { _source?.Dispose(); } catch { /* ignore */ }
        _source = null;
        try { _session?.Dispose(); } catch { /* ignore */ }
        _session = null;
    }

    private void OnTcpRecv(TcpIpTraceData e) { if (e.size > 0) Add(e.ProcessID, e.size, rx: true); }
    private void OnTcpSend(TcpIpSendTraceData e) { if (e.size > 0) Add(e.ProcessID, e.size, rx: false); }
    private void OnTcpRecvV6(TcpIpV6TraceData e) { if (e.size > 0) Add(e.ProcessID, e.size, rx: true); }
    private void OnTcpSendV6(TcpIpV6SendTraceData e) { if (e.size > 0) Add(e.ProcessID, e.size, rx: false); }
    private void OnUdpRecv(UdpIpTraceData e) { if (e.size > 0) Add(e.ProcessID, e.size, rx: true); }
    private void OnUdpSend(UdpIpTraceData e) { if (e.size > 0) Add(e.ProcessID, e.size, rx: false); }

    private void Add(int pid, long size, bool rx)
    {
        var c = _counters.GetOrAdd(pid, static _ => new Counters());
        if (rx) Interlocked.Add(ref c.Rx, size);
        else Interlocked.Add(ref c.Tx, size);
        c.LastMs = _clock.ElapsedMilliseconds;
    }

    private string NameOf(int pid) =>
        _nameByPid.GetOrAdd(pid, static p =>
        {
            try { return Process.GetProcessById(p).ProcessName.ToLowerInvariant(); }
            catch { return ""; }
        });

    private static bool PidAlive(int pid)
    {
        try { Process.GetProcessById(pid); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        Stop();
    }
}
