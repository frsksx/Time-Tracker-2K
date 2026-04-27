$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishExe = Join-Path $repoRoot 'bin\Release\net9.0-windows\win-x64\publish\TimeTracker2K.exe'
$outputDir = Join-Path $PSScriptRoot 'out'
$payloadDir = Join-Path $PSScriptRoot 'portable-payload'
$zipPath = Join-Path $outputDir 'TimeTracker2K-v1.0.0-portable.zip'

if (-not (Test-Path -LiteralPath $publishExe)) {
    throw "Published executable not found. Run dotnet publish first: $publishExe"
}

Remove-Item -LiteralPath $payloadDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Copy-Item -LiteralPath $publishExe -Destination (Join-Path $payloadDir 'TimeTracker2K.exe') -Force

$readme = @'
Time Tracker 2K portable

Run TimeTracker2K.exe to start the tray app.

No admin rights are required. No installer script is included in this ZIP.
Tracking data is stored locally under:
%LOCALAPPDATA%\TimeTracker2K\work-log.json

To uninstall the portable copy, exit the tray app and delete this folder.
Local tracking data is not deleted automatically.
'@
Set-Content -LiteralPath (Join-Path $payloadDir 'README.txt') -Value $readme -Encoding ASCII

Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $payloadDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

Get-Item -LiteralPath $zipPath
