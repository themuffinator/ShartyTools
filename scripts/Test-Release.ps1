#Requires -Version 7.0
param([string]$ExpectedVersion, [switch]$CheckGit)
. (Join-Path $PSScriptRoot 'Common.ps1')
$version = Get-ProjectVersion
if ($ExpectedVersion -and $ExpectedVersion -cne $version.Version) { throw "Requested $ExpectedVersion but version.txt contains $($version.Version)." }
$changelog = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot 'CHANGELOG.md') -Raw
$entry = [regex]::Match($changelog, '(?ms)^## \[' + [regex]::Escape($version.Version) + '\][^\r\n]*\r?\n(?<notes>.*?)(?=^## |\z)')
if (-not $entry.Success -or -not $entry.Groups['notes'].Value.Trim()) { throw "CHANGELOG.md needs a nonempty [$($version.Version)] section." }
if ($CheckGit) {
    Push-Location $script:RepositoryRoot
    try {
        $changes = & git status --porcelain
        if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Git status.' }
        if ($changes) { throw 'Release validation requires a clean Git tree.' }
        $existing = & git tag --list $version.Tag
        if ($LASTEXITCODE -ne 0) { throw 'Unable to check existing tags.' }
        if ($existing) { throw "Tag $($version.Tag) already exists. Versions are immutable." }
    } finally { Pop-Location }
}
[pscustomobject]@{ Version = $version.Version; Tag = $version.Tag; Prerelease = $version.Prerelease; Notes = $entry.Groups['notes'].Value.Trim() }
