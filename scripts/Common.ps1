#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Initialize-Workspace {
    $taskTemporary = Join-Path $script:RepositoryRoot '.agents/tmp/build'
    [IO.Directory]::CreateDirectory($taskTemporary) | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $taskTemporary 'dotnet'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $taskTemporary 'http'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
}

function Get-ProjectVersion {
    param([string]$Value = (Get-Content -LiteralPath (Join-Path $script:RepositoryRoot 'version.txt') -Raw).Trim())
    $pattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?$'
    if ($Value -cnotmatch $pattern) { throw "version.txt must contain SemVer without a v prefix or build metadata: $Value" }
    [pscustomobject]@{ Version = $Value; Tag = "v$Value"; Prerelease = $Value.Contains('-') }
}

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

function Remove-DisposableDirectory {
    param([Parameter(Mandatory)][string]$Path)
    $resolved = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath((Join-Path $script:RepositoryRoot '.agents/tmp')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup target is outside .agents/tmp: $resolved" }
    if (-not (Test-Path -LiteralPath $resolved)) { return }
    $ancestor = $resolved
    while ($ancestor) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cleanup ancestor is a link: $ancestor" }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($resolved)
    while ($pending.Count) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cleanup target contains a link: $($item.FullName)" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
