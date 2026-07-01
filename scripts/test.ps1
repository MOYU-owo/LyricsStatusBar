[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { (Get-Command dotnet -ErrorAction Stop).Source }

& $dotnet build (Join-Path $root 'LyricsStatusBar.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
& $dotnet run --project (Join-Path $root 'tests\LyricsStatusBar.Tests\LyricsStatusBar.Tests.csproj') -c Release --no-build
exit $LASTEXITCODE
