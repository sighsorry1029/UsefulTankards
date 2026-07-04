param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir,

    [Parameter(Mandatory = $true)]
    [string]$TargetPath,

    [Parameter(Mandatory = $true)]
    [string]$TargetDir,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyName
)

$ErrorActionPreference = "Stop"

$version = [System.Reflection.AssemblyName]::GetAssemblyName($TargetPath).Version.ToString(3)
$packageDir = Join-Path $ProjectDir "Thunderstore"
$manifestPath = Join-Path $packageDir "manifest.json"
$iconPath = Join-Path $packageDir "icon.png"
$readmePath = Join-Path $ProjectDir "README.md"
$changelogPath = Join-Path $packageDir "CHANGELOG.md"

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.version_number = $version
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Remove-Item -LiteralPath (Join-Path $TargetDir "ThunderstorePackage") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $TargetDir "Libs") -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath $TargetDir -Filter "$AssemblyName-*.zip" -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -LiteralPath $packageDir -Filter "$AssemblyName-*.zip" -File -ErrorAction SilentlyContinue | Remove-Item -Force

$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) ($AssemblyName + "-Thunderstore-" + [System.Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    Copy-Item -LiteralPath $TargetPath -Destination (Join-Path $stagingDir ([System.IO.Path]::GetFileName($TargetPath))) -Force
    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $stagingDir "README.md") -Force
    Copy-Item -LiteralPath $changelogPath -Destination (Join-Path $stagingDir "CHANGELOG.md") -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDir "manifest.json") -Force
    Copy-Item -LiteralPath $iconPath -Destination (Join-Path $stagingDir "icon.png") -Force

    $zipPath = Join-Path $packageDir ($AssemblyName + "-" + $version + ".zip")

    $packageFiles = @(
        Join-Path $stagingDir ([System.IO.Path]::GetFileName($TargetPath))
        Join-Path $stagingDir "README.md"
        Join-Path $stagingDir "CHANGELOG.md"
        Join-Path $stagingDir "manifest.json"
        Join-Path $stagingDir "icon.png"
    )
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $zipPath -Force
    Write-Host "Created Thunderstore package: $zipPath"
}
finally {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
}
