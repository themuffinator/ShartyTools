#Requires -Version 7.0
param([Parameter(Mandatory)][string]$PublishDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$destination = [IO.Path]::GetFullPath((Join-Path $PublishDirectory 'licenses'))
[IO.Directory]::CreateDirectory($destination) | Out-Null
Copy-Item -Path (Join-Path $script:RepositoryRoot 'third_party/*') -Destination $destination -Recurse
$packageKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($depsFile in Get-ChildItem -LiteralPath $PublishDirectory -Filter '*.deps.json') {
    $deps = Get-Content -LiteralPath $depsFile.FullName -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in $deps.libraries.Keys) {
        if ($deps.libraries[$key].type -eq 'package') { [void]$packageKeys.Add($key) }
    }
}
$inventory = foreach ($key in ($packageKeys | Sort-Object)) {
    $directory = Join-Path $script:RepositoryRoot ('.artifacts/packages/' + $key.ToLowerInvariant())
    $specFile = Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' | Select-Object -First 1
    [xml]$spec = Get-Content -LiteralPath $specFile.FullName -Raw
    $metadata = $spec.package.metadata
    $packageDestination = Join-Path $destination ($key.Replace('/', '-'))
    [IO.Directory]::CreateDirectory($packageDestination) | Out-Null
    foreach ($notice in Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $_.Name -match '(?i)license|licence|notice|copying' }) {
        $relative = [IO.Path]::GetRelativePath($directory, $notice.FullName)
        $target = Join-Path $packageDestination $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $target
    }
    # Nuspec retains exact package identity, authors, license and source repository.
    Copy-Item -LiteralPath $specFile.FullName -Destination $packageDestination
    [pscustomobject]@{ Package = $key; License = $metadata.SelectSingleNode('*[local-name()="license"]')?.InnerText; Source = $metadata.SelectSingleNode('*[local-name()="repository"]')?.GetAttribute('url') }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'dependencies.json') -Encoding utf8
