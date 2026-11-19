param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root 'src\TargetMarkerOverlay\bin\Release\net48\TargetMarkerOverlay.dll'
$updater = Join-Path $root 'src\TargetMarkerOverlay.Updater\bin\Release\net48\TargetMarkerOverlay.Updater.exe'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build the Release DLL before packaging.' }

$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
if (-not $fileVersion.StartsWith($Version + '.', [StringComparison]::Ordinal)) { throw "DLL version $fileVersion does not match $Version." }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($updater).FileVersion -ne ($Version + '.0')) { throw 'Updater version mismatch.' }
if ([Reflection.AssemblyName]::GetAssemblyName($updater).Name -ne 'TargetMarkerOverlay.Updater') { throw 'Unexpected updater assembly.' }

$output = Join-Path $root 'release'
$stage = Join-Path $output ('TargetMarkerOverlay-v' + $Version)
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'TargetMarkerOverlay.dll') -Force
Copy-Item -LiteralPath $updater -Destination (Join-Path $stage 'TargetMarkerOverlay.Updater.exe') -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $stage 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_LICENSES.md') -Destination (Join-Path $stage 'THIRD_PARTY_LICENSES.md') -Force

$zip = Join-Path $output ('TargetMarkerOverlay-v' + $Version + '.zip')
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
$packageFiles = @('TargetMarkerOverlay.dll', 'TargetMarkerOverlay.Updater.exe', 'README.md', 'THIRD_PARTY_LICENSES.md') | ForEach-Object { Join-Path $stage $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
Write-Host "Created $zip"
