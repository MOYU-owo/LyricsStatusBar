[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$artifacts = Join-Path $root 'artifacts'
$portableZip = Join-Path $artifacts 'LyricsStatusBar-Windows-x64-portable.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.File]::Delete($portableZip)
[IO.Compression.ZipFile]::CreateFromDirectory(
    (Join-Path $artifacts 'app'),
    $portableZip,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$iscc = if ($env:ISCC_EXE -and (Test-Path -LiteralPath $env:ISCC_EXE)) {
    $env:ISCC_EXE
} else {
    @(
        (Join-Path $root 'tools\InnoSetup\ISCC.exe'),
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $iscc) {
    Write-Warning "Inno Setup 6 was not found. Portable package created: $portableZip"
    exit 2
}
& $iscc (Join-Path $root 'installer\LyricsStatusBar.iss')
exit $LASTEXITCODE