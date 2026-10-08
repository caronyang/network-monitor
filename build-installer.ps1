<#
  Network Monitor - build installers (x64 + x86) with Inno Setup 6.

  Usage:
    .\build-installer.ps1                 full build: publish self-contained
                                           payloads for win-x64/win-x86, render
                                           installer\NetworkMonitor.iss per arch
                                           and compile with ISCC.exe
    .\build-installer.ps1 -SkipPublish    reuse publish\installer\win-* payloads
    .\build-installer.ps1 -Arch x86       build a single architecture

  Output:
    publish\installer\NetworkMonitor-<ver>-x64-setup.exe
    publish\installer\NetworkMonitor-<ver>-x86-setup.exe
#>
param(
    [string]$Version = "1.0.0",
    [ValidateSet("x64", "x86", "both")]
    [string]$Arch = "both",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "src\NetworkMonitor\NetworkMonitor.csproj"
$template = Join-Path $root "installer\NetworkMonitor.iss"
$outDir = Join-Path $root "publish\installer"

if (-not (Test-Path $template)) { throw "template not found: $template" }

# --- locate ISCC.exe (Inno Setup 6) -----------------------------------------
$iscc = @(
    Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe not found - install Inno Setup 6 first" }

function Publish-Payload([string]$arch) {
    $out = Join-Path $outDir "win-$arch"
    Write-Host "==> dotnet publish win-$arch (self-contained) -> $out"
    & dotnet publish $proj -c Release -r "win-$arch" --self-contained true -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish win-$arch failed (exit $LASTEXITCODE)" }
}

function New-RenderedIss([string]$arch) {
    $allowed = if ($arch -eq "x64") { "x64compatible" } else { "x86compatible" }
    $mode64  = if ($arch -eq "x64") { "ArchitecturesInstallIn64BitMode=x64compatible" } else { "" }
    $map = [ordered]@{
        "@APP_VERSION@"     = $Version
        "@APP_ARCH@"        = $arch
        "@ARCH_ALLOWED@"    = $allowed
        "@ARCH_64BIT_MODE@" = $mode64
        "@SOURCE_DIR@"      = Join-Path $outDir "win-$arch"
        "@OUTPUT_DIR@"      = $outDir
        "@ICON_FILE@"       = Join-Path $root "src\NetworkMonitor\assets\app.ico"
        "@LICENSE_FILE@"    = Join-Path $root "LICENSE"
    }

    $text = [System.IO.File]::ReadAllText($template)
    foreach ($k in $map.Keys) { $text = $text.Replace($k, [string]$map[$k]) }

    # Any leftover @TOKEN@ means the template and script are out of sync.
    $leftovers = [regex]::Matches($text, "@[A-Z_]+@") | ForEach-Object { $_.Value } | Sort-Object -Unique
    if ($leftovers) { throw "unreplaced template tokens: $($leftovers -join ', ')" }

    $generated = Join-Path $outDir "NetworkMonitor-$arch.iss"
    # UTF-8 WITH BOM: the script contains Traditional Chinese messages.
    [System.IO.File]::WriteAllText($generated, $text, [System.Text.UTF8Encoding]::new($true))
    return $generated
}

$archList = if ($Arch -eq "both") { @("x64", "x86") } else { @($Arch) }
foreach ($a in $archList) {
    if (-not $SkipPublish) { Publish-Payload $a }
    else {
        $payload = Join-Path $outDir "win-$a"
        if (-not (Test-Path (Join-Path $payload "NetworkMonitor.exe"))) {
            throw "-SkipPublish set but payload missing: $payload (run without -SkipPublish)"
        }
    }
    $iss = New-RenderedIss $a
    Write-Host "==> ISCC $iss"
    & $iscc $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed for $a (exit $LASTEXITCODE)" }
}

Write-Host ""
Write-Host "INSTALLERS OK:"
Get-ChildItem $outDir -Filter "NetworkMonitor-*-setup.exe" |
    Sort-Object Name |
    ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
