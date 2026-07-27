[CmdletBinding()]
param(
	[switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot 'dist'
$packageRoot = Join-Path $distRoot 'MapReroll'

if (-not $SkipBuild) {
	& dotnet build (Join-Path $repoRoot 'Source\MapReroll.csproj') -c Release
	if ($LASTEXITCODE -ne 0) {
		throw "Release build failed with exit code $LASTEXITCODE."
	}
}

if (Test-Path -LiteralPath $packageRoot) {
	$resolvedPackageRoot = (Resolve-Path -LiteralPath $packageRoot).Path
	if (-not $resolvedPackageRoot.StartsWith($distRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
		throw "Refusing to remove package outside dist: $resolvedPackageRoot"
	}
	Remove-Item -LiteralPath $resolvedPackageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$runtimeItems = @(
	'1.4',
	'1.5',
	'1.6',
	'About',
	'Common',
	'LoadFolders.xml'
)

foreach ($item in $runtimeItems) {
	$source = Join-Path $repoRoot $item
	if (-not (Test-Path -LiteralPath $source)) {
		throw "Required runtime item is missing: $source"
	}
	Copy-Item -LiteralPath $source -Destination $packageRoot -Recurse -Force
}

Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter '*.pdb' |
	Remove-Item -Force

[xml] $about = Get-Content -LiteralPath (Join-Path $repoRoot 'About\About.xml') -Raw
$versionMatch = [regex]::Match([string] $about.ModMetaData.description, 'Version:\s*([^\r\n]+)')
if (-not $versionMatch.Success) {
	throw 'Could not read the release version from About.xml.'
}

$archivePath = Join-Path $distRoot ("MapReroll-{0}.zip" -f $versionMatch.Groups[1].Value.Trim())
if (Test-Path -LiteralPath $archivePath) {
	Remove-Item -LiteralPath $archivePath -Force
}

Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal

Write-Output "Workshop folder: $packageRoot"
Write-Output "Release archive: $archivePath"
