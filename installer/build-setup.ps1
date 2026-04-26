$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishExe = Join-Path $repoRoot 'bin\Release\net9.0-windows\win-x64\publish\TimeTracker2K.exe'
$payloadDir = Join-Path $PSScriptRoot 'payload'
$outputDir = Join-Path $PSScriptRoot 'out'
$sedPath = Join-Path $PSScriptRoot 'TimeTracker2KSetup.sed'
$setupPath = Join-Path $outputDir 'TimeTracker2KSetup.exe'

function Wait-SetupPackaging {
    param([datetime] $StartedAt)

    $deadline = (Get-Date).AddMinutes(5)
    do {
        $activePackaging = Get-Process -Name iexpress, makecab -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $StartedAt.AddSeconds(-2) }
        if (-not $activePackaging) {
            return
        }

        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    throw 'Timed out waiting for IExpress packaging to finish.'
}

if (-not (Test-Path -LiteralPath $publishExe)) {
    throw "Published executable not found. Run dotnet publish first: $publishExe"
}

New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Copy-Item -LiteralPath $publishExe -Destination (Join-Path $payloadDir 'TimeTracker2K.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.cmd') -Destination (Join-Path $payloadDir 'install.cmd') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination (Join-Path $payloadDir 'install.ps1') -Force

$escapedPayloadDir = $payloadDir.TrimEnd('\') + '\'
$escapedSetupPath = $setupPath

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=$escapedSetupPath
FriendlyName=Time Tracker 2K Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles

[Strings]
InstallPrompt=Install Time Tracker 2K for the current Windows user?
DisplayLicense=
FinishMessage=Time Tracker 2K setup has finished.
FILE0="TimeTracker2K.exe"
FILE1="install.cmd"
FILE2="install.ps1"

[SourceFiles]
SourceFiles0=$escapedPayloadDir

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@

Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

$iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe'
if (-not (Test-Path -LiteralPath $iexpress)) {
    throw "IExpress was not found at $iexpress"
}

Remove-Item -LiteralPath $setupPath -Force -ErrorAction SilentlyContinue
$startedAt = Get-Date
& $iexpress /N /Q $sedPath
Wait-SetupPackaging -StartedAt $startedAt

if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "IExpress did not create the setup executable. Exit code: $LASTEXITCODE"
}

$setupItem = Get-Item -LiteralPath $setupPath
if ($setupItem.Length -le 0) {
    throw "IExpress created an empty setup executable. Exit code: $LASTEXITCODE"
}

$setupItem
