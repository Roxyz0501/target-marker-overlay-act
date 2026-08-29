$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '3.8.5.288'
$expectedHash = '1a0ca91375d79daf3d2cbbb2920c90dbc40f3be2421cdc6765d93e27bf25e4b0'
$assetUrl = 'https://github.com/EQAditu/AdvancedCombatTracker/releases/download/3.8.5.288/ACTv3.zip'

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$workRoot = Join-Path $tempRoot 'target-marker-overlay-act-act-reference'
$archive = Join-Path $workRoot 'ACTv3.zip'
$extractRoot = Join-Path $workRoot 'extracted'

if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

Invoke-WebRequest -Uri $assetUrl -OutFile $archive

$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "ACT archive hash mismatch. Expected $expectedHash, got $actualHash."
}

Expand-Archive -LiteralPath $archive -DestinationPath $extractRoot

$actFiles = @(
    Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter 'Advanced Combat Tracker.exe'
)
if ($actFiles.Count -ne 1) {
    throw "Expected exactly one Advanced Combat Tracker.exe, found $($actFiles.Count)."
}

$actPath = $actFiles[0].FullName
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($actPath).FileVersion
if ($fileVersion -ne $version) {
    throw "Unexpected ACT version. Expected $version, got $fileVersion."
}

Write-Output $actPath
