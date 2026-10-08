# Network Monitor

**English** | [繁體中文](README.zh-TW.md)

A lightweight real-time network traffic monitor that lives in the Windows system tray:
single executable, low CPU and memory usage, logging per-interface traffic to SQLite with
built-in line/bar chart statistics. UI available in English and Traditional Chinese,
switchable at runtime.

![Main window](screenshots/main.png)

## Features

| Area | Description |
|---|---|
| Real-time monitoring | Samples download/upload rate of every network interface every second; updates the main grid and tray tooltip live |
| Tray resident | Closing the window minimizes to the tray and keeps running; tray tooltip shows the current total rate |
| Floating panel | Semi-transparent always-on-top mini panel with live rates, **today's totals** and a 90-second sparkline (draggable, double-click opens main window, right-click menu) |
| Statistics | **Line chart**: day (hourly) / week (daily) / month (daily); **bar chart**: day/week/month totals. Filter by interface, navigate periods with ◀▶, export CSV |
| Logging | One row per interface per minute in SQLite (WAL mode), configurable retention (default 180 days) |
| Startup | One-click "run at logon" via the `HKCU\...\Run` registry value |
| Languages | English / 繁體中文 switch instantly from the tray menu or Settings, no restart needed |
| Low resource | See the [Performance](#performance) section |

## Screenshots

| Statistics (English) | Floating panel |
|---|---|
| ![Statistics](screenshots/stats-en.png) | ![Floating panel](screenshots/panel.png) |

| Statistics (繁體中文) | |
|---|---|
| ![Statistics ZH](screenshots/stats-zh.png) | |

## Requirements

- Windows 10 (build 19041 or later) or Windows 11, **x64 (recommended) or x86**
- **Installer / self-contained build**: nothing else to install
- **Portable build**: .NET 10 Desktop Runtime ([download](https://dotnet.microsoft.com/download/dotnet/10.0))

## Installation

### Installer (recommended)

1. Download `NetworkMonitor-1.0.0-x64-setup.exe` from the
   [Releases](https://github.com/caronyang/network-monitor/releases) page
   (use `-x86-setup.exe` for 32-bit systems)
2. Run it. The installer offers:
   - Start Menu shortcut (always) and an optional desktop shortcut
   - Optional **Launch automatically at Windows sign-in** — writes the same registry
     value the app's own autostart toggle uses
   - English and Traditional Chinese installer UI
3. Uninstall later via *Windows Settings → Apps*.
   Your data (`%LOCALAPPDATA%\NetworkMonitor`) is **kept** on uninstall.

### Portable

Download `NetworkMonitor-1.0.0-portable-x64.zip` from the
[Releases](https://github.com/caronyang/network-monitor/releases) page, extract it and
run `NetworkMonitor.exe` (requires the .NET 10 Desktop Runtime).

To build it yourself, run `publish-portable.bat` (framework-dependent) or
`publish-selfcontained.bat` (runs anywhere), then start `NetworkMonitor.exe` from the
output folder. Both scripts accept `x64` or `x86` as an argument (default `x64`).

## Usage

### Main window

- **Toolbar**: `Statistics` opens the charts, `Settings` opens preferences.
  Switch language from the tray menu or Settings; minimize with the native window buttons.
- **Dark summary bar**: total live rate, **Today** (daily total, refreshed from the
  database every 45 s) and **Session** (since app start).
- **Interface grid**: each row shows the interface name, link status and link speed,
  live download/upload rates with relative progress bars, and today/session totals.

### System tray

- **Double-click**: show the main window
- **Right-click menu**: show main window / statistics / floating panel / autostart /
  language / settings / exit
- **Hover tooltip**: current total download/upload rate (updated every 5 s)

### Statistics

- Chart type: `Line` or `Bar`
- Period: `Day` (by hour 00–23), `Week` (by day), `Month` (by day, 30 days)
- Interface: all interfaces or a single one
- `<` `>` switch to the previous/next period; `F5` / `←` `→` work too
- `Export CSV` exports the visible range

### Floating panel

A semi-transparent topmost mini panel showing live download/upload rates,
**today's totals** and a 90-second sparkline. Drag to move, double-click to open the
main window, right-click for a menu, `×` or the menu item to close. Position is remembered.

![Floating panel](screenshots/panel.png)

## Settings and data location

```
%LOCALAPPDATA%\NetworkMonitor\
  networkmonitor.db     SQLite database (WAL)
  settings.json         settings
  crash.log             error log
```

`settings.json` main fields:

| Field | Meaning | Default |
|---|---|---|
| `language` | `en` / `zh` | `en` |
| `autoStart` | run at logon | `false` |
| `floatingPanel` | show floating panel | `false` |
| `sampleIntervalMs` | sampling interval (200–60000) | `1000` |
| `retentionDays` | raw sample retention in days (1–3650) | `180` |
| `includeLoopback` | include 127.0.0.1 | `false` |
| `includeDisconnected` | include disconnected interfaces | `false` |
| `showFilterAdapters` | show packet-filter interfaces | `false` |

### About interface filtering (important)

A typical Windows install has 50+ "hidden" interfaces (WFP, Npcap, QoS and antivirus
filter adapters) that **mirror the same traffic** — showing them all counts the same
packets several times.

The app therefore, by default:

1. **Excludes packet-filter interfaces** (`MIB_IF_ROW2.Flags` bit 1) to avoid double
   counting — can be enabled in Settings
2. **Excludes loopback and disconnected interfaces** — can be enabled in Settings
3. **Excludes RAS/VPN WAN Miniports** (Windows auto-named `Local Area Connection* N`) —
   they always look connected but never carry traffic

So the list normally only contains real physical/virtual NICs such as "Ethernet"/"Wi-Fi".

## Performance

Measured on this machine (Windows 10, Release build, desktop Intel CPU):

| State | CPU | Memory |
|---|---|---|
| Main window visible | about 1.7 – 8 % (depends on repaint frequency driven by live traffic; 1.7 – 1.9 % measured at low traffic) | about 65 – 75 MB |
| Main window hidden (tray only) | about 0.04 – 0.2 % (0.12 % measured) | about 67 MB |

With the main window visible, CPU is spent on "value changed → repaint grid", so it
correlates with live traffic: more traffic → more repaints → more CPU. Around 1.7 % at
low traffic, up to about 8 % under sustained high traffic.

Key design points for low resource usage:

- Sampling uses `GetIfTable2` P/Invoke (~1 ms per call, ~47× faster than the
  `NetworkInterface` API)
- Sampling runs on a dedicated low-priority thread; the UI is driven by a single 1 Hz
  timer with no message-pump busy loop
- The custom-drawn grid only repaints when **values actually change**; brushes/pens are cached
- SQLite commits one small transaction per minute (WAL) — no background write spikes
- Tray tooltip updates every 5 s (only visible while hovered)

## Building from source

Requires [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
(and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer):

```bat
build.bat                          :: Release build (src\NetworkMonitor\bin\Release\...)
publish-portable.bat [x64|x86]     :: framework-dependent folder (~46 MB, needs .NET Desktop Runtime)
publish-selfcontained.bat [x64|x86]:: self-contained folder (~163 MB, runs anywhere)
build-installer.bat                :: Inno Setup installers for x64 + x86 -> publish\installer\
```

Open `NetworkMonitor.sln` in Visual Studio, or run directly:

```powershell
dotnet run --project src\NetworkMonitor -c Release
```

## Development tools (tools/)

| File | Purpose |
|---|---|
| `make_icon.ps1` | generates `assets\app.ico` (7 sizes) |
| `build.bat` / `publish-*.bat` / `build-installer.*` | build, publish and installer scripts (project root) |
| `measure.ps1` | CPU measurement per mode (`NM_NOPAINT` diagnostic hooks) |
| `IfTableTest\` | correctness/performance comparison of `GetIfTable2` vs `NetworkInterface` |
| `ApiDump\` | Windows API struct field dump (development) |
| `ApiIntrospect\` | ScottPlot API reflection explorer |
| `shot.ps1` `shoth.ps1` `shotpw.ps1` `opengraphics.ps1` | UI screenshots (`shotpw` uses PrintWindow, unaffected by occlusion) |
| `enumwin.ps1` `uiclick.ps1` `uidump.ps1` `uicheck.ps1` `uicombo.ps1` `clickkeys.ps1` `sendkeys2.ps1` `uielement.ps1` `uiclose.ps1` | window enumeration / button clicks / element dumps / checkbox & combo operations (testing) |
| `clickbtn.ps1` | click a window button by index (language-agnostic) |
| `asciiart.ps1` `rawprof.ps1` `textink.ps1` `pixcheck.ps1` `measuretext.ps1` | pixel verification: region→ASCII / row ink profile / button text position / grid spacing / TextRenderer measurement |
| `dumpstat.py` | parse `uidump.ps1` output |
| `cleanup_db.py` | remove duplicate interface rows from the database |
| `dllcheck.py` | check build output contains expected strings (verify a build took effect) |

Diagnostic environment variables (do not set for normal use):

- `NM_NOPAINT=1` (skip the 1 Hz repaint) or `grid` / `summary` / `tray` (skip a single
  repaint source) — isolate drawing cost for performance analysis
- `NM_STATS="chart=bar period=week offset=-1"` opens the statistics window in a given
  state (`chart`: `line`|`bar`; `period`: `day`|`week`|`month`; `offset`: integer,
  0 = current period, -1 = previous) to reproduce screen states

## Troubleshooting

- **App does not start / crashes**: check `%LOCALAPPDATA%\NetworkMonitor\crash.log`
- **My interface is missing from the list**: Settings → Monitoring, enable
  "include disconnected interfaces" or "include packet-filter interfaces"
- **Numbers differ from Task Manager**: Task Manager shows per-process traffic, this app
  shows total traffic **per network interface** (all processes)
- **Keeps old data after reinstalling Windows**: data lives in
  `%LOCALAPPDATA%\NetworkMonitor\` — keep the folder and the app will continue where it left off

## Tech stack

- C# / .NET 10 WinForms (`net10.0-windows10.0.19041`, Windows desktop only, x64/x86)
- [ScottPlot 5.1.59](https://scottplot.net/) charts
- [Microsoft.Data.Sqlite 10.0.12](https://learn.microsoft.com/ef/core/providers/sqlite/) + SQLite WAL
- Windows `GetIfTable2` (iphlpapi) P/Invoke sampling

## License

[MIT](LICENSE) © 2026 Caron Yang
