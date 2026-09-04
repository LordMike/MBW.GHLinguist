[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string] $Package,

  [Parameter(Mandatory)]
  [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageMatches = @(Resolve-Path -Path $Package)
if ($packageMatches.Count -ne 1) {
  throw "Expected exactly one NuGet package matching '$Package', found $($packageMatches.Count)."
}

$packagePath = $packageMatches[0].Path
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
  $entries = @($archive.Entries.FullName)
  $requiredEntries = @(
    'README.md'
    'lib/net10.0/MBW.GHLinguist.dll'
    'lib/net10.0/MBW.GHLinguist.xml'
    'buildTransitive/MBW.GHLinguist.targets'
    'runtimes/linux-x64/native/ghlinguist.so'
    'runtimes/win-x64/native/ghlinguist.dll'
    'nativeassets/linux-x64/provenance.json'
    'nativeassets/win-x64/provenance.json'
    'nativeassets/linux-x64/licenses/MBW.GHLinguist/LICENSE'
    'nativeassets/linux-x64/licenses/MBW.GHLinguist/THIRD-PARTY-NOTICES.md'
    'nativeassets/linux-x64/licenses/ruby/COPYING'
    'nativeassets/linux-x64/licenses/linguist/LICENSE'
    'nativeassets/win-x64/licenses/MBW.GHLinguist/LICENSE'
    'nativeassets/win-x64/licenses/MBW.GHLinguist/THIRD-PARTY-NOTICES.md'
    'nativeassets/win-x64/licenses/ruby/COPYING'
    'nativeassets/win-x64/licenses/rubyinstaller/LICENSE'
    'nativeassets/win-x64/licenses/linguist/LICENSE'
    'nativeassets/win-x64/licenses/msys2/icu/LICENSE'
    'nativeassets/win-x64/licenses/msys2/gcc/COPYING3'
    'nativeassets/win-x64/licenses/msys2/gcc/COPYING.RUNTIME'
    'nativeassets/win-x64/licenses/msys2/winpthreads/COPYING'
  )

  foreach ($requiredEntry in $requiredEntries) {
    if ($requiredEntry -notin $entries) {
      throw "Package is missing required entry: $requiredEntry"
    }
  }

  foreach ($rid in 'linux-x64', 'win-x64') {
    $prefix = "nativeassets/$rid/"
    $provenancePath = $prefix + 'provenance.json'
    $provenanceEntries = @($archive.Entries | Where-Object { $_.FullName -eq $provenancePath })
    if ($provenanceEntries.Count -ne 1) {
      throw "Package native closure for $rid must contain exactly one provenance file."
    }

    $reader = [System.IO.StreamReader]::new($provenanceEntries[0].Open())
    try {
      $provenance = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
      $reader.Dispose()
    }

    if ($provenance.platform -ne $rid) {
      throw "Package native closure provenance platform '$($provenance.platform)' does not match $rid."
    }

    $provenanceFiles = @($provenance.files)
    if ($provenanceFiles.Count -eq 0) {
      throw "Package native closure provenance for $rid does not describe any files."
    }

    $hasNestedFile = $false
    foreach ($provenanceFile in $provenanceFiles) {
      $relativePath = [string] $provenanceFile.path
      if ([string]::IsNullOrWhiteSpace($relativePath) -or
          [System.IO.Path]::IsPathRooted($relativePath) -or
          $relativePath.Replace('\', '/').Split('/') -contains '..') {
        throw "Package native closure provenance for $rid contains an invalid path '$relativePath'."
      }

      $normalizedPath = $relativePath.Replace('\', '/')
      $assetPath = $prefix + $normalizedPath
      $assetEntries = @($archive.Entries | Where-Object { $_.FullName -eq $assetPath })
      if ($assetEntries.Count -ne 1) {
        throw "Package native closure provenance for $rid references missing asset: $assetPath"
      }

      if ($normalizedPath.Contains('/')) {
        $hasNestedFile = $true
      }

      $stream = $assetEntries[0].Open()
      $hash = [System.Security.Cryptography.SHA256]::Create()
      try {
        $actualHash = [System.Convert]::ToHexString($hash.ComputeHash($stream)).ToLowerInvariant()
      }
      finally {
        $hash.Dispose()
        $stream.Dispose()
      }

      if ($actualHash -ne $provenanceFile.sha256) {
        throw "Package native closure asset hash does not match provenance: $assetPath"
      }
    }

    if (-not $hasNestedFile) {
      throw "Package native closure for $rid does not preserve nested asset layout."
    }

    $gemLicenseEntries = @($entries | Where-Object { $_ -like "$prefix`licenses/gems/*" })
    if ($gemLicenseEntries.Count -eq 0) {
      throw "Package native closure for $rid does not contain staged gem license files."
    }
  }

  $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
  if ($nuspecEntries.Count -ne 1) {
    throw "Expected exactly one nuspec entry, found $($nuspecEntries.Count)."
  }

  $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
  try {
    [xml] $nuspec = $reader.ReadToEnd()
  }
  finally {
    $reader.Dispose()
  }

  $versionNode = $nuspec.SelectSingleNode(
    "/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='version']")
  if (-not $versionNode -or $versionNode.InnerText -ne $ExpectedVersion) {
    $actualVersion = if ($versionNode) { $versionNode.InnerText } else { '<missing>' }
    throw "Expected package version '$ExpectedVersion', found '$actualVersion'."
  }
}
finally {
  $archive.Dispose()
}

Write-Host "Validated NuGet package: $packagePath"
