$ErrorActionPreference = 'Stop'

$displayName = 'Time Tracker 2K'
$processName = 'TimeTracker2K'
$valueName = 'TimeTracker2K'
$legacyValueName = 'LoginDurationTracker'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\TimeTracker2K'
$sourceExe = Join-Path $PSScriptRoot 'TimeTracker2K.exe'
$targetExe = Join-Path $installDir 'TimeTracker2K.exe'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Time Tracker 2K'
$shortcutPath = Join-Path $startMenuDir 'Time Tracker 2K.lnk'
$uninstallShortcutPath = Join-Path $startMenuDir 'Uninstall Time Tracker 2K.lnk'
$uninstallScriptPath = Join-Path $installDir 'Uninstall Time Tracker 2K.cmd'

if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Installer payload is missing: $sourceExe"
}

Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force

$uninstallScript = @"
@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-Process -Name '$processName' -ErrorAction SilentlyContinue ^| Stop-Process -Force; Remove-ItemProperty -Path '$runKeyPath' -Name '$valueName' -ErrorAction SilentlyContinue; Remove-ItemProperty -Path '$runKeyPath' -Name '$legacyValueName' -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '$shortcutPath' -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '$uninstallShortcutPath' -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '$targetExe' -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '$uninstallScriptPath' -Force -ErrorAction SilentlyContinue; if (Test-Path -LiteralPath '$installDir') { Remove-Item -LiteralPath '$installDir' -Force -ErrorAction SilentlyContinue }; Write-Host '$displayName was uninstalled. Local tracking data was kept in %LOCALAPPDATA%\TimeTracker2K.'"
"@
Set-Content -LiteralPath $uninstallScriptPath -Value $uninstallScript -Encoding ASCII

New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$shell = New-Object -ComObject WScript.Shell

$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = $displayName
$shortcut.Save()

$uninstallShortcut = $shell.CreateShortcut($uninstallShortcutPath)
$uninstallShortcut.TargetPath = $uninstallScriptPath
$uninstallShortcut.WorkingDirectory = $installDir
$uninstallShortcut.Description = "Uninstall $displayName"
$uninstallShortcut.Save()

if (-not (Test-Path $runKeyPath)) {
    New-Item -Path $runKeyPath -Force | Out-Null
}

$runItem = Get-ItemProperty -Path $runKeyPath -ErrorAction SilentlyContinue
$currentRunValue = $runItem.$valueName
$legacyRunValue = $runItem.$legacyValueName
if (-not [string]::IsNullOrWhiteSpace($currentRunValue) -or -not [string]::IsNullOrWhiteSpace($legacyRunValue)) {
    Set-ItemProperty -Path $runKeyPath -Name $valueName -Value "`"$targetExe`""
    Remove-ItemProperty -Path $runKeyPath -Name $legacyValueName -ErrorAction SilentlyContinue
}

Start-Process -FilePath $targetExe
