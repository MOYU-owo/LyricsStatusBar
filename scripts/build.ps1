[CmdletBinding()]
param([switch]$SkipNative)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$appOutput = Join-Path $artifacts 'app'
$pluginOutput = Join-Path $artifacts 'plugin'
$nativeIntermediate = Join-Path $artifacts 'native'
$pluginPackage = Join-Path $artifacts 'LyricsStatusBarBridge.plugin'
$payloadOutput = Join-Path $appOutput 'BetterNCM-Plugin'

function Resolve-DotNet {
    if ($env:DOTNET_EXE -and (Test-Path -LiteralPath $env:DOTNET_EXE)) {
        return $env:DOTNET_EXE
    }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    $local = Get-Item (Join-Path $env:TEMP 'LyricsStatusBarBuild\tools\dotnet\dotnet.exe') -ErrorAction SilentlyContinue
    if ($local) {
        return $local.FullName
    }
    throw 'The .NET 10 SDK was not found. Set DOTNET_EXE or install the SDK.'
}

function Resolve-NativeCompiler {
    if ($env:LLVM_MINGW_ROOT) {
        $candidate = Join-Path $env:LLVM_MINGW_ROOT 'bin\i686-w64-mingw32-clang.exe'
        if (Test-Path -LiteralPath $candidate) {
            return [PSCustomObject]@{ Kind = 'Clang'; Path = $candidate }
        }
    }

    $candidate = Get-ChildItem (Join-Path $env:TEMP 'LyricsStatusBarBuild\llvm-mingw\*\bin\i686-w64-mingw32-clang.exe') -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidate) {
        return [PSCustomObject]@{ Kind = 'Clang'; Path = $candidate.FullName }
    }

    $msvc = Get-Command cl.exe -ErrorAction SilentlyContinue
    if ($msvc) {
        return [PSCustomObject]@{ Kind = 'Msvc'; Path = $msvc.Source }
    }

    throw 'No x86 native compiler was found. Set LLVM_MINGW_ROOT, enter an x86 MSVC developer shell, or pass -SkipNative.'
}

New-Item -ItemType Directory -Force $appOutput | Out-Null
New-Item -ItemType Directory -Force $pluginOutput | Out-Null
New-Item -ItemType Directory -Force $nativeIntermediate | Out-Null
New-Item -ItemType Directory -Force $payloadOutput | Out-Null

Copy-Item -LiteralPath (Join-Path $root 'plugin\manifest.json') -Destination (Join-Path $pluginOutput 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $root 'plugin\plugin.js') -Destination (Join-Path $pluginOutput 'plugin.js') -Force

if (-not $SkipNative) {
    $compiler = Resolve-NativeCompiler
    $nativeOutput = Join-Path $pluginOutput 'native.dll'
    $nativeSource = Join-Path $root 'plugin\native\bridge.c'
    if ($compiler.Kind -eq 'Clang') {
        & $compiler.Path '-shared' '-Os' '-s' '-Wl,--no-insert-timestamp' '-o' $nativeOutput $nativeSource '-lkernel32'
    } else {
        $objectOutput = Join-Path $nativeIntermediate 'bridge.obj'
        $importLibrary = Join-Path $nativeIntermediate 'native.lib'
        $definitionFile = Join-Path $root 'plugin\native\bridge.def'
        & $compiler.Path '/nologo' '/LD' '/O1' '/MT' "/Fo$objectOutput" $nativeSource '/link' "/OUT:$nativeOutput" "/IMPLIB:$importLibrary" "/DEF:$definitionFile" 'kernel32.lib'
    }
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $pluginOutput 'native.dll'))) {
    throw 'native.dll is missing. Build the native bridge before packaging.'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.File]::Delete($pluginPackage)
$pluginArchive = [IO.Compression.ZipFile]::Open($pluginPackage, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($fileName in @('manifest.json', 'plugin.js', 'native.dll')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $pluginArchive,
            (Join-Path $pluginOutput $fileName),
            $fileName,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $pluginArchive.Dispose()
}

$dotnet = Resolve-DotNet
$publishArguments = @(
    'publish',
    (Join-Path $root 'src\LyricsStatusBar.App\LyricsStatusBar.App.csproj'),
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--nologo',
    '--configfile', (Join-Path $root 'NuGet.Config'),
    '--source', 'https://api.nuget.org/v3/index.json',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:SatelliteResourceLanguages=zh-Hans',
    '-o', $appOutput
)
& $dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

[IO.File]::Delete((Join-Path $appOutput 'LyricsStatusBar.pdb'))
[IO.File]::Delete((Join-Path $appOutput 'LyricsStatusBar.Core.pdb'))

foreach ($fileName in @('manifest.json', 'plugin.js', 'native.dll')) {
    Copy-Item -LiteralPath (Join-Path $pluginOutput $fileName) -Destination (Join-Path $payloadOutput $fileName) -Force
}
Copy-Item -LiteralPath $pluginPackage -Destination (Join-Path $payloadOutput 'LyricsStatusBarBridge.plugin') -Force
foreach ($document in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $document) -Destination (Join-Path $appOutput $document) -Force
}

Write-Host "Self-contained Windows x64 build complete: $artifacts"
