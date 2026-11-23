[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'GetSenderId_TgBot\GetSenderId_TgBot.csproj'
[xml]$project = Get-Content -Raw -LiteralPath $projectPath
$version = [string]$project.Project.PropertyGroup.Version

if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'The project Version property is missing.'
}

if ($Tag -cne "v$version") {
    throw "Tag '$Tag' does not match project version '$version'."
}

$runtime = 'win-x64'
$outputPath = Join-Path $PSScriptRoot "GetSenderId-TgBot-$version-$runtime"
if (Test-Path -LiteralPath $outputPath) {
    throw "Release output already exists: $outputPath"
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime $runtime `
    --self-contained true `
    --output $outputPath `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $outputPath 'GetSenderId_TgBot.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable is missing: $executablePath"
}

$versionInfo = (Get-Item -LiteralPath $executablePath).VersionInfo
if ($versionInfo.ProductVersion -ne $version -and $versionInfo.ProductVersion -notlike "$version+*") {
    throw "Published product version '$($versionInfo.ProductVersion)' does not match '$version'."
}

$hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $outputPath 'GetSenderId_TgBot.exe.sha256'
[IO.File]::WriteAllText(
    $checksumPath,
    "$hash  GetSenderId_TgBot.exe$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))

Write-Host "Release $version created at $outputPath"
