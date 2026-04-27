$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot 'bin\Release\net9.0-windows\win-x64\publish'
$publishExe = Join-Path $publishDir 'TimeTracker2K.exe'
$wixSource = Join-Path $PSScriptRoot 'msi\TimeTracker2K.wxs'
$outputDir = Join-Path $PSScriptRoot 'out'
$msiPath = Join-Path $outputDir 'TimeTracker2K-1.0.0.msi'
$wixExe = Join-Path $repoRoot '.tools\wix.exe'

if (-not (Test-Path -LiteralPath $publishExe)) {
    throw "Published executable not found. Run dotnet publish first: $publishExe"
}

if (-not (Test-Path -LiteralPath $wixExe)) {
    New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot '.tools') | Out-Null
    dotnet tool install wix --tool-path (Join-Path $repoRoot '.tools')
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install the WiX local tool."
    }
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

& $wixExe eula accept wix7
if ($LASTEXITCODE -ne 0) {
    throw "Could not accept the WiX EULA for this local build."
}

& $wixExe build $wixSource -arch x64 -d "PublishDir=$publishDir" -o $msiPath
if ($LASTEXITCODE -ne 0) {
    throw "WiX failed with exit code $LASTEXITCODE"
}

Get-Item -LiteralPath $msiPath
