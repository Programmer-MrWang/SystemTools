param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ClassIslandDebugDirectory
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    throw "Plugin output directory was not found: $OutputDirectory"
}

if (-not (Test-Path -LiteralPath $ClassIslandDebugDirectory)) {
    throw "ClassIsland Debug output was not found: $ClassIslandDebugDirectory. Run tools/plugin/build.ps1 first."
}

$hostFileNames = @(
    Get-ChildItem -LiteralPath $ClassIslandDebugDirectory -File -Recurse |
        ForEach-Object { $_.Name }
)

if ($hostFileNames.Count -eq 0) {
    return
}

Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse |
    Where-Object { $hostFileNames -contains $_.Name } |
    Remove-Item -Force -ErrorAction SilentlyContinue
