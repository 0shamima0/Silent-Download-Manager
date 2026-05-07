param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,

    [ValidateSet("Chrome", "Edge")]
    [string]$Browser = "Chrome",

    [string]$NativeHostPath = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($NativeHostPath)) {
    $NativeHostPath = Join-Path $root "build\publish\SDM\SDM.NativeHost.exe"
}

if (-not (Test-Path $NativeHostPath)) {
    throw "Native host exe not found: $NativeHostPath. Run scripts\build-release.ps1 first."
}

$manifestTemplate = if ($Browser -eq "Chrome") {
    Join-Path $root "extensions\native-messaging\com.sis.sdm.chrome.json"
} else {
    Join-Path $root "extensions\native-messaging\com.sis.sdm.edge.json"
}

$manifestDir = Join-Path $env:LOCALAPPDATA "SiS SDM\NativeMessaging"
$manifestPath = Join-Path $manifestDir "com.sis.sdm.json"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null

$escapedHostPath = $NativeHostPath.Replace("\", "\\")
$manifest = Get-Content -Raw -Path $manifestTemplate
$manifest = $manifest.Replace("__NATIVE_HOST_EXE_PATH__", $escapedHostPath)
$manifest = $manifest.Replace("__EXTENSION_ID__", $ExtensionId)
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

$registryPath = if ($Browser -eq "Chrome") {
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.sis.sdm"
} else {
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.sis.sdm"
}

New-Item -Path $registryPath -Force | Out-Null
Set-Item -Path $registryPath -Value $manifestPath

Write-Host "$Browser native host registered."
Write-Host "Manifest: $manifestPath"

