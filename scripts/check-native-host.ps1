param(
    [ValidateSet("Chrome", "Edge")]
    [string]$Browser = "Chrome"
)

$registryPath = if ($Browser -eq "Chrome") {
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.sis.sdm"
} else {
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.sis.sdm"
}

if (-not (Test-Path $registryPath)) {
    Write-Host "$Browser native host is not registered."
    exit 1
}

$manifestPath = (Get-Item -Path $registryPath).GetValue("")

if ([string]::IsNullOrWhiteSpace($manifestPath)) {
    Write-Host "$Browser native host registry key exists, but default manifest path is empty."
    exit 1
}

Write-Host "$Browser native host manifest: $manifestPath"

if (-not (Test-Path $manifestPath)) {
    Write-Host "Manifest file does not exist."
    exit 1
}

$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
Write-Host "Native host exe: $($manifest.path)"

if (-not (Test-Path $manifest.path)) {
    Write-Host "Native host exe does not exist."
    exit 1
}

Write-Host "$Browser native host registration looks OK."

