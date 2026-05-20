param(
    [string]$SimHubPath = "",
    [string]$MozaSdkPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($SimHubPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:SIMHUB_PATH)) {
        $SimHubPath = $env:SIMHUB_PATH
    } else {
        $SimHubPath = "C:\Program Files (x86)\SimHub"
    }
}

if ([string]::IsNullOrWhiteSpace($MozaSdkPath) -and -not [string]::IsNullOrWhiteSpace($env:MOZA_SDK_PATH)) {
    $MozaSdkPath = $env:MOZA_SDK_PATH
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Missing required dependency: $Source"
    }

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $DestinationDirectory -Force
    Write-Host "Copied $Source -> $DestinationDirectory"
}

$simHubDestination = Join-Path $repoRoot "libs\SimHub"
foreach ($file in @("SimHub.Plugins.dll", "GameReaderCommon.dll", "SimHub.Logging.dll", "Newtonsoft.Json.dll")) {
    Copy-RequiredFile -Source (Join-Path $SimHubPath $file) -DestinationDirectory $simHubDestination
}

if ([string]::IsNullOrWhiteSpace($MozaSdkPath)) {
    throw "MOZA SDK path is required. Pass -MozaSdkPath or set MOZA_SDK_PATH to the SDK_CSharp\x86 folder containing MOZA_API_CSharp.dll."
}

$mozaDestination = Join-Path $repoRoot "libs\MOZA_SDK"
foreach ($file in @("MOZA_API_CSharp.dll", "MOZA_API_C.dll", "MOZA_SDK.dll")) {
    Copy-RequiredFile -Source (Join-Path $MozaSdkPath $file) -DestinationDirectory $mozaDestination
}

Write-Host "Dependency cache is ready."
