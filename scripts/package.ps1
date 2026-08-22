param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $repository 'artifacts'
$publish = Join-Path $artifacts 'publish'

if (-not $publish.StartsWith($repository, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish directory escaped the repository.'
}

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path $publish -Force | Out-Null

dotnet publish (Join-Path $repository 'src\SysWatt.App\SysWatt.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true --no-restore `
    -p:Version=$Version -p:PublishSingleFile=true -p:PublishTrimmed=false `
    --output $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

New-Item -ItemType File -Path (Join-Path $publish 'portable.flag') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination $publish
Copy-Item -LiteralPath (Join-Path $repository 'THIRD-PARTY-NOTICES.md') -Destination $publish

$zip = Join-Path $artifacts "SysWatt-$Version-win-x64-portable.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal

$compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $compiler) {
    $knownLocations = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $compiler = $knownLocations | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if ($compiler) {
    & $compiler "/DMyAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $repository 'installer\SysWatt.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
} else {
    Write-Warning 'ISCC.exe was not found; portable package created, installer skipped.'
}

$checksumFile = Join-Path $artifacts 'SHA256SUMS.txt'
$releaseFiles = Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Extension -in '.zip', '.exe' }
$lines = foreach ($file in $releaseFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$($file.Name)"
}
Set-Content -LiteralPath $checksumFile -Value $lines -Encoding utf8NoBOM
Write-Host "Release artifacts written to $artifacts"
