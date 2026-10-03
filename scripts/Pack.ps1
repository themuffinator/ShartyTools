#Requires -Version 7.0
param([ValidateSet('win-x64', 'linux-x64')][string]$Runtime = 'win-x64', [switch]$Release)
. (Join-Path $PSScriptRoot 'Common.ps1')
Initialize-Workspace
$version = (Get-ProjectVersion).Version
if (-not $Release) { $version += '-dev' }
$name = "ShartyTools-$version-$Runtime"
$stage = Join-Path $script:RepositoryRoot ('.agents/tmp/pack/' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $stage $name
$out = Join-Path $script:RepositoryRoot '.artifacts/releases'
$extension = if ($Runtime.StartsWith('win')) { '.zip' } else { '.tar.gz' }
$archive = Join-Path $out ($name + $extension)
$temporaryArchive = Join-Path $stage ($name + $extension)
if (Test-Path -LiteralPath $archive) { throw "Release archive already exists: $archive. Move it aside before building again." }
[IO.Directory]::CreateDirectory($publish) | Out-Null
[IO.Directory]::CreateDirectory($out) | Out-Null
Push-Location $script:RepositoryRoot
try {
    $releaseProperty = if ($Release) { '-p:ReleaseBuild=true' } else { '-p:ReleaseBuild=false' }
    foreach ($project in @('src/ShartyTools.Desktop/ShartyTools.Desktop.csproj', 'src/ShartyTools.Cli/ShartyTools.Cli.csproj')) {
        Invoke-Dotnet publish $project -c Release -r $Runtime --self-contained true -o $publish $releaseProperty '-p:RestoreLockedMode=true' '-p:PublishTrimmed=false'
    }
    foreach ($file in @('LICENSE', 'README.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath $file -Destination $publish }
    Copy-Item -LiteralPath (Join-Path $script:RepositoryRoot 'docs') -Destination $publish -Recurse
    & (Join-Path $PSScriptRoot 'Collect-Notices.ps1') -PublishDirectory $publish
    if ($Runtime.StartsWith('win')) {
        [IO.Compression.ZipFile]::CreateFromDirectory($publish, $temporaryArchive, [IO.Compression.CompressionLevel]::Optimal, $true)
    } else {
        if (-not $IsLinux) { throw 'Create Linux archives on Linux so executable permissions are preserved.' }
        & tar -czf $temporaryArchive -C $stage $name
        if ($LASTEXITCODE -ne 0) { throw 'tar failed.' }
    }
    [IO.File]::Move($temporaryArchive, $archive, $false)
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($archive + '.sha256', "$hash  $([IO.Path]::GetFileName($archive))`n")
    Write-Output "Created $archive"
} finally {
    Pop-Location
    Remove-DisposableDirectory -Path $stage
}
