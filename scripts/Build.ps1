#Requires -Version 7.0
param([switch]$CaptureUi)
. (Join-Path $PSScriptRoot 'Common.ps1')
Initialize-Workspace
Push-Location $script:RepositoryRoot
try {
    if ($CaptureUi) { $env:SHARTY_CAPTURE_UI = '1' }
    Invoke-Dotnet restore ShartyTools.slnx --locked-mode
    Invoke-Dotnet build ShartyTools.slnx -c Release --no-restore
    Invoke-Dotnet test ShartyTools.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory .artifacts/test-results
    & (Join-Path $PSScriptRoot 'Test-Release.ps1') | Format-List Version, Tag, Prerelease
} finally { Pop-Location }
