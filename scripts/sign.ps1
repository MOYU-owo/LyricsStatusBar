[CmdletBinding()]
param([Parameter(Mandatory)][string[]]$Files)

$ErrorActionPreference = 'Stop'
$certificateBase64 = $env:WINDOWS_SIGNING_CERTIFICATE_BASE64
$certificatePassword = $env:WINDOWS_SIGNING_CERTIFICATE_PASSWORD
if ([string]::IsNullOrWhiteSpace($certificateBase64)) {
    Write-Host 'No trusted signing certificate secret was configured; Authenticode signing skipped.'
    exit 0
}

$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
if (-not $signtool) {
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $kitsRoot = Join-Path $programFilesX86 'Windows Kits\10\bin'
    $signtool = Get-ChildItem $kitsRoot -Filter signtool.exe -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\signtool.exe' } |
        Sort-Object FullName -Descending |
        Select-Object -ExpandProperty FullName -First 1
}
if (-not $signtool) {
    throw 'signtool.exe was not found.'
}

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }
$certificatePath = Join-Path $tempRoot ("LyricsStatusBar-" + [Guid]::NewGuid().ToString('N') + '.pfx')
try {
    [IO.File]::WriteAllBytes($certificatePath, [Convert]::FromBase64String($certificateBase64))
    foreach ($file in $Files) {
        $resolvedFile = (Resolve-Path -LiteralPath $file).Path
        & $signtool sign /fd SHA256 /td SHA256 /tr 'http://timestamp.digicert.com' /f $certificatePath /p $certificatePassword $resolvedFile
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
        & $signtool verify /pa /v $resolvedFile
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
} finally {
    [IO.File]::Delete($certificatePath)
}