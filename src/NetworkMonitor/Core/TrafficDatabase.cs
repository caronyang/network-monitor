using Microsoft.Data.Sqlite;

namespace NetworkMonitor.Core;

/// <summary>One aggregated traffic row (per interface, per local minute).</summary>
public readonly record struct TrafficRow(string Iface, long Ts, int Day, int Hour, long Rx, long Tx);

/// <summary>Aggregated chart point; <see cref="Key"/> is an hour (0-23) or a yyyyMMdd day key.</summary>
public readonly record struct Bucket(int Key, long Rx, long Tx);

/// <summary>One traffic-stopwatch record: totals between two local timestamps (unix seconds).</summary>
public readonly record struct Measurement(long StartUnix, long EndUnix, long Rx, long Tx);

public readonly record struct DayTotal(long Rx, long Tx);

/// <summary>
/// SQLite persistence. A single write connection (owned by the sampler thread) plus
/// short-lived read connections (WAL allows readers while the writer works).
/// </summary>
public sealed class TrafficDatabase : IDisposable
{
    private readonly string _path;
    private readonly object _writeGate = new();
    private SqliteConnection? _write;
    private bool _disposed;

    public TrafficDatabase(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public string FilePath => _path;

    public long FileSizeBytes
    {
        get
        {
            try { return new FileInfo(_path).Length; }
            catch { return 0; }
        }
    }

    private static string BuildString(string source) => new SqliteConnectionStringBuilder
    {
        DataSource = source,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true,
    }.ToString();

    private static void Pragma(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Configure(SqliteConnection conn, bool setJournalMode)
    {
        if (setJournalMode) Pragma(conn, "PRAGMA journal_mode=WAL;");
        Pragma(conn, "PRAGMA synchronous=NORMAL;");
        Pragma(conn, "PRAGMA busy_timeout=5000;");
        Pragma(conn, "PRAGMA temp_store=MEMORY;");
        Pragma(conn, "PRAGMA cache_size=-4000;");
        Pragma(conn, "PRAGMA mmap_size=134217728;");
    }

    public void EnsureSchema()
    {
        using var conn = new SqliteConnection(BuildString(_path));
        conn.Open();
        Configure(conn, true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS traffic (
                iface TEXT    NOT NULL,
                ts    INTEGER NOT NULL,
                day   INTEGER NOT NULL,
                hr    INTEGER NOT NULL,
                rx    INTEGER NOT NULL DEFAULT 0,
                tx    INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (iface, ts)
            ) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS ix_traffic_day ON traffic(day);
            CREATE TABLE IF NOT EXISTS measurement (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                start_ts INTEGER NOT NULL,
                end_ts   INTEGER NOT NULL,
                rx       INTEGER NOT NULL DEFAULT 0,
                tx       INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_measurement_start ON measurement(start_ts);
            CREATE TABLE IF NOT EXISTS meta (
                k TEXT PRIMARY KEY,
                v TEXT NOT NULL
            );
            INSERT OR IGNORE INTO meta(k, v) VALUES ('schema_version', '1');
            INSERT OR REPLACE INTO meta(k, v) VALUES ('schema_version', '2');
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenWrite()
    {
        if (_write is null || _write.State != System.Data.ConnectionState.Open)
        {
            _write?.Dispose();
            _write = new SqliteConnection(BuildString(_path));
            _write.Open();
            Configure(_write, false);
        }

        return _write;
    }

    private SqliteConnection OpenRead()
    {
        var conn = new SqliteConnection(BuildString(_path));
        conn.Open();
        Configure(conn, false);
        return conn;
    }

    /// <summary>Writes a batch of minute buckets in a single transaction. Sampler thread only.</summary>
    public void WriteBuckets(IReadOnlyList<TrafficRow> rows)
    {
        if (rows.Count == 0 || _disposed) return;

        lock (_writeGate)
        {
            var conn = OpenWrite();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO traffic(iface, ts, day, hr, rx, tx)
                VALUES ($i, $t, $d, $h, $r, $s)
                ON CONFLICT(iface, ts) DO UPDATE SET
                    rx = rx + excluded.rx,
                    tx = tx + excluded.tx;
                """;
            var p = cmd.Parameters;
            p.Add("$i", SqliteType.Text);
            p.Add("$t", SqliteType.Integer);
            p.Add("$d", SqliteType.Integer);
            p.Add("$h", SqliteType.Integer);
            p.Add("$r", SqliteType.Integer);
            p.Add("$s", SqliteType.Integer);
            cmd.Prepare();

            foreach (var row in rows)
            {
                p["$i"].Value = row.Iface;
                p["$t"].Value = row.Ts;
                p["$d"].Value = row.Day;
                p["$h"].Value = row.Hour;
                p["$r"].Value = row.Rx;
                p["$s"].Value = row.Tx;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    /// <summary>Removes rows older than the retention window. Returns deleted row count.</summary>
    public int Prune(int retentionDays)
    {
        if (_disposed) return 0;
        var cutoff = DateUtil.DayKey(DateTime.Today.AddDays(-Math.Max(1, retentionDays)));
        long cutoffUnix = new DateTimeOffset(DateUtil.FromDayKey(cutoff)).ToUnixTimeSeconds();
        lock (_writeGate)
        {
            var conn = OpenWrite();
            int deleted;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM traffic WHERE day < $c;";
                cmd.Parameters.AddWithValue("$c", cutoff);
                deleted = cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM measurement WHERE end_ts < $c;";
                cmd.Parameters.AddWithValue("$c", cutoffUnix);
                cmd.ExecuteNonQuery();
            }

            return deleted;
        }
    }

    /// <summary>Appends one traffic-stopwatch record.</summary>
    public void WriteMeasurement(Measurement m)
    {
        if (_disposed) return;
        lock (_writeGate)
        {
            var conn = OpenWrite();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO measurement(start_ts, end_ts, rx, tx) VALUES ($a, $b, $r, $t);";
            cmd.Parameters.AddWithValue("$a", m.StartUnix);
            cmd.Parameters.AddWithValue("$b", m.EndUnix);
            cmd.Parameters.AddWithValue("$r", m.Rx);
            cmd.Parameters.AddWithValue("$t", m.Tx);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Newest stopwatch records first.</summary>
    public List<Measurement> QueryMeasurements(int limit = 200)
    {
        var list = new List<Measurement>();
        try
        {
            using var conn = OpenRead();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT start_ts, end_ts, rx, tx FROM measurement ORDER BY start_ts DESC LIMIT $n;";
            cmd.Parameters.AddWithValue("$n", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(new Measurement(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
        }
        catch
        {
            // unreadable database -> empty list
        }

        return list;
    }

    /// <summary>All process keys that have recorded data (newest day first).</summary>
    public List<string> ProcessKeys()
    {
        var list = new List<string>();
        try
        {
            using var conn = OpenRead();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT iface FROM traffic WHERE iface LIKE 'P:%' ORDER BY iface;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(reader.GetString(0));
        }
        catch
        {
            // ignore
        }

        return list;
    }

    public long Count()
    {
        try
        {
            using var conn = OpenRead();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM traffic;";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        catch
        {
            return 0;
        }
    }

    public void Clear()
    {
        lock (_writeGate)
        {
            var conn = OpenWrite();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM traffic; DELETE FROM measurement;";
            cmd.ExecuteNonQuery();
            Pragma(conn, "PRAGMA wal_checkpoint(TRUNCATE);");
        }
    }

    /// <summary>Hourly buckets for one local day (key = 0..23).</summary>
    public List<Bucket> QueryDay(int dayKey, string? iface)
    {
        var result = new List<Bucket>(24);
        try
        {
            using var conn = OpenRead();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = iface is null
                ? "SELECT hr, SUM(rx), SUM(tx) FROM traffic WHERE day = $d AND iface NOT LIKE 'P:%' GROUP BY hr ORDER BY hr;"
                : "SELECT hr, SUM(rx), SUM(tx) FROM traffic WHERE day = $d AND iface = $i GROUP BY hr ORDER BY hr;";
            cmd.Parameters.AddWithValue("$d", dayKey);
            if (iface is not null) cmd.Parameters.AddWithValue("$i", iface);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(new Bucket(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2)));
            }
        }
        catch
        {
            // database unreadable -> empty chart
        }

        return result;
    }

    /// <summary>Daily buckets for an inclusive day range (key = yyyyMMdd).</summary>
    public List<Bucket> QueryDays(int fromDayKey, int toDayKey, string? iface)
    {
        var result = new List<Bucket>(64);
        try
        {
            using var conn = OpenRead();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = iface is null
                ? "SELECT day, SUM(rx), SUM(tx) FROM traffic WHERE day >= $a AND day <= $b AND iface NOT LIKE 'P:%' GROUP BY day ORDER BY day;"
                : "SELECT day, SUM(rx), SUM(tx) FROM traffic WHERE day >= $a AND day <= $b AND iface = $i GROUP BY day ORDER BY day;";
            cmd.Parameters.AddWithValue("$a", fromDayKey);
            cmd.Parameters.AddWithValue("$b", toDayKey);
            if (iface is not null) cmd.Parameters.AddWithValue("$i", iface);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    result.Add(new Bucket(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2)));
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }

    /// <summary>Totals per interface for one local day.</summary>
    public Dictionary<string, DayTotal> TotalsPerInterface(int dayKey)
    {
        var map = new Dictionary<string, DayTotal>(StringComparer.Ordinal);
        try
        {
            using var conn = OpenRead();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT iface, SUM(rx), SUM(tx) FROM traffic WHERE day = $d GROUP BY iface;";
            cmd.Parameters.AddWithValue("$d", dayKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetString(0)] = new DayTotal(reader.GetInt64(1), reader.GetInt64(2));
        }
        catch
        {
            // ignore
        }

        return map;
    }

    public DayTotal Total(int dayKey, string? iface)
    {
        long rx = 0, tx = 0;
        try
        {
            using var conn = OpenRead();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = iface is null
                ? "SELECT COALESCE(SUM(rx),0), COALESCE(SUM(tx),0) FROM traffic WHERE day = $d AND iface NOT LIKE 'P:%';"
                : "SELECT COALESCE(SUM(rx),0), COALESCE(SUM(tx),0) FROM traffic WHERE day = $d AND iface = $i;";
            cmd.Parameters.AddWithValue("$d", dayKey);
            if (iface is not null) cmd.Parameters.AddWithValue("$i", iface);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                rx = reader.GetInt64(0);
                tx = reader.GetInt64(1);
            }
        }
        catch
        {
            // ignore
        }

        return new DayTotal(rx, tx);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_writeGate)
        {
            try { _write?.Dispose(); }
            catch { /* ignore */ }
            _write = null;
        }
    }
}
