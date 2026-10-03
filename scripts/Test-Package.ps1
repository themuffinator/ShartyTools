#Requires -Version 7.0
param([Parameter(Mandatory)][string]$Archive)
. (Join-Path $PSScriptRoot 'Common.ps1')
$Archive = [IO.Path]::GetFullPath($Archive)
$stage = Join-Path $script:RepositoryRoot ('.agents/tmp/package-test/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
try {
    $expected, $name = (Get-Content -LiteralPath ($Archive + '.sha256') -Raw).Trim() -split '  ', 2
    if ($name -cne [IO.Path]::GetFileName($Archive)) { throw 'Checksum names a different archive.' }
    if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) { throw 'Archive checksum mismatch.' }
    if ($Archive.EndsWith('.zip')) {
        [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $stage)
    } else {
        $stream = [IO.File]::OpenRead($Archive)
        try {
            $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress)
            try { [System.Formats.Tar.TarFile]::ExtractToDirectory($gzip, $stage, $false) }
            finally { $gzip.Dispose() }
        } finally { $stream.Dispose() }
    }
    $directories = @(Get-ChildItem -LiteralPath $stage -Directory)
    if ($directories.Count -ne 1) { throw 'Expected one application folder in the archive.' }
    $application = $directories[0].FullName
    $suffix = if ($IsWindows) { '.exe' } else { '' }
    foreach ($file in @("ShartyTools$suffix", "sharty$suffix", 'LICENSE', 'licenses/dependencies.json', 'docs/RELEASING.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $application $file))) { throw "Package is missing $file" }
    }
    $dependencies = Get-Content -LiteralPath (Join-Path $application 'licenses/dependencies.json') -Raw | ConvertFrom-Json
    $runtimes = @($dependencies | Where-Object Package -match '^Microsoft\.NETCore\.App\.Runtime\.')
    if ($runtimes.Count -ne 1) { throw 'Expected exactly one bundled .NET runtime notice set.' }
    $runtimeNotices = Join-Path $application ('licenses/' + $runtimes[0].Package.Replace('/', '-'))
    foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        if (-not (Test-Path -LiteralPath (Join-Path $runtimeNotices $notice))) { throw "Missing .NET runtime notice: $notice" }
    }
    $cli = Join-Path $application "sharty$suffix"
    $version = & $cli --version
    if ($LASTEXITCODE -ne 0 -or -not $version.StartsWith((Get-ProjectVersion).Version)) { throw 'Packaged CLI version check failed.' }
    & $cli --help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Packaged CLI help failed.' }
    $project = Join-Path $stage 'smoke.sharty.json'
    & $cli jam new $project smoke 'Smoke Jam'
    if ($LASTEXITCODE -ne 0) { throw 'Packaged project creation failed.' }
    & $cli jam check $project --json | Out-Null
    if ($LASTEXITCODE -ne 1) { throw 'Empty jam should produce QA exit code 1.' }
    & $cli md2 inspect (Join-Path $stage 'missing.md2') 2>$null
    if ($LASTEXITCODE -ne 2) { throw 'Missing input should produce exit code 2.' }
    # Finish with a successful native command so GitHub's pwsh wrapper exits 0.
    & $cli --version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Final packaged CLI check failed.' }
    Write-Output "Package smoke test passed: $version"
} finally { Remove-DisposableDirectory -Path $stage }
