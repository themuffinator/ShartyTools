#Requires -Version 7.0
param([Parameter(Mandatory)][string]$Version)
. (Join-Path $PSScriptRoot 'Common.ps1')
$validated = Get-ProjectVersion -Value $Version
[IO.File]::WriteAllText((Join-Path $script:RepositoryRoot 'version.txt'), $validated.Version + "`n")
Write-Output "Set version to $($validated.Version). Add its entry to CHANGELOG.md before releasing."
