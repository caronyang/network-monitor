namespace NetworkMonitor.Core;

/// <summary>
/// Diagnostic switches driven by environment variables (never exposed in the UI).
/// Used to isolate per-frame costs when profiling:
/// <list type="bullet">
///   <item><c>NM_NOPAINT=1</c> - skip every 1 Hz repaint (summary, grid, sparkline).</item>
///   <item><c>NM_NOPAINT=grid</c> / <c>summary</c> / <c>tray</c> - skip a single repaint source.</item>
/// </list>
/// Undefined / any other value means normal operation.
/// </summary>
internal static class DebugFlags
{
    private static readonly string Mode = Environment.GetEnvironmentVariable("NM_NOPAINT") ?? string.Empty;

    /// <summary>Skip all paint-only invalidations.</summary>
    public static readonly bool NoPaint = Mode == "1";

    /// <summary>Skip the interface grid repaint.</summary>
    public static readonly bool NoPaintGrid = Mode is "1" or "grid";

    /// <summary>Skip the summary band repaint.</summary>
    public static readonly bool NoPaintSummary = Mode is "1" or "summary";

    /// <summary>Skip updating the tray icon tooltip.</summary>
    public static readonly bool NoTrayText = Mode is "1" or "tray";
}
