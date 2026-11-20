param(
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/Modernizr.DiffViewer/Modernizr.DiffViewer.csproj'
$output = Join-Path $PSScriptRoot "artifacts/publish/$Runtime"
dotnet publish $project -c Release -r $Runtime --self-contained -o $output
if ($LASTEXITCODE -ne 0) { throw "DiffViewer publish failed with exit code $LASTEXITCODE." }
Write-Host "Published $output"
