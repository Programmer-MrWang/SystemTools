param(
    [string]$ClassIslandRoot = "E:\ClassIsland-git-misha",
    [string]$PowerShellDirectory = "D:\PowerShell-7.7.0-preview.4-win-x64"
)

$ErrorActionPreference = "Stop"

$desktopRoot = Join-Path $ClassIslandRoot "ClassIsland.Desktop"
$debugDirectory = Join-Path $desktopRoot "bin\Debug\net10.0-windows10.0.19041.0"
$debugBinary = Join-Path $debugDirectory "ClassIsland.Desktop.exe"

if (-not (Test-Path -LiteralPath $debugBinary)) {
    throw "ClassIsland Debug binary was not found at $debugBinary. Build ClassIsland.Desktop first."
}

[Environment]::SetEnvironmentVariable("ClassIsland_DebugBinaryFile", $debugBinary, "User")
[Environment]::SetEnvironmentVariable("ClassIsland_DebugBinaryDirectory", $debugDirectory, "User")
$env:ClassIsland_DebugBinaryFile = $debugBinary
$env:ClassIsland_DebugBinaryDirectory = $debugDirectory

$powerShellExecutable = Join-Path $PowerShellDirectory "pwsh.exe"
if (-not (Test-Path -LiteralPath $powerShellExecutable)) {
    throw "PowerShell executable was not found at $powerShellExecutable. Pass -PowerShellDirectory with the installed PowerShell Core directory."
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$pathEntries = @($userPath -split ';' | Where-Object { $_ })
if ($pathEntries -notcontains $PowerShellDirectory) {
    [Environment]::SetEnvironmentVariable("Path", (($pathEntries + $PowerShellDirectory) -join ';'), "User")
}
$env:Path = (($env:Path -split ';' | Where-Object { $_ } | Where-Object { $_ -ne $PowerShellDirectory }) + $PowerShellDirectory) -join ';'

Write-Host "ClassIsland debug environment configured." -ForegroundColor Green
Write-Host "Binary:    $debugBinary"
Write-Host "Directory: $debugDirectory"
Write-Host "PowerShell: $powerShellExecutable"
Write-Host "Restart Rider or Visual Studio after running this script so it can read the updated user environment."
