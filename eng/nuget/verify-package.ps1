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
  $fileEntries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
  $entries = @($fileEntries.FullName)

  foreach ($entry in $entries) {
    $segments = $entry.Split('/')
    if ([string]::IsNullOrWhiteSpace($entry) -or
        $entry.Contains('\') -or
        $entry.StartsWith('/') -or
        $entry -match '^[A-Za-z]:' -or
        $segments -contains '' -or
        $segments -contains '.' -or
        $segments -contains '..') {
      throw "Package contains an invalid entry path: $entry"
    }
  }

  $entryNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in $entries) {
    if (-not $entryNames.Add($entry)) {
      throw "Package contains duplicate or case-colliding entry paths: $entry"
    }
  }

  $requiredEntries = @(
    'README.md'
    'LICENSE'
    'THIRD-PARTY-NOTICES.md'
    'icon.png'
    'lib/net10.0/MBW.GHLinguist.dll'
    'lib/net10.0/MBW.GHLinguist.xml'
    'buildTransitive/MBW.GHLinguist.targets'
    'native/include/ghlinguist.h'
    'runtimes/linux-x64/native/ghlinguist.so'
    'runtimes/win-x64/native/ghlinguist.dll'
    'nativeassets/linux-x64/provenance.json'
    'nativeassets/win-x64/provenance.json'
    'nativeassets/linux-x64/licenses/MBW.GHLinguist/LICENSE'
    'nativeassets/linux-x64/licenses/MBW.GHLinguist/THIRD-PARTY-NOTICES.md'
    'nativeassets/linux-x64/licenses/ruby/COPYING'
    'nativeassets/linux-x64/licenses/ruby/BSDL'
    'nativeassets/linux-x64/licenses/ruby/LEGAL'
    'nativeassets/linux-x64/licenses/linguist/LICENSE'
    'nativeassets/win-x64/licenses/MBW.GHLinguist/LICENSE'
    'nativeassets/win-x64/licenses/MBW.GHLinguist/THIRD-PARTY-NOTICES.md'
    'nativeassets/win-x64/licenses/ruby/COPYING'
    'nativeassets/win-x64/licenses/ruby/BSDL'
    'nativeassets/win-x64/licenses/ruby/LEGAL'
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
    $provenanceEntries = @($fileEntries | Where-Object { $_.FullName -ceq $provenancePath })
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
    if ($provenance.schemaVersion -ne 2) {
      throw "Package native closure provenance for $rid has unsupported schema version '$($provenance.schemaVersion)'."
    }

    $provenanceFiles = @($provenance.files)
    if ($provenanceFiles.Count -eq 0) {
      throw "Package native closure provenance for $rid does not describe any files."
    }

    $provenancePaths = [System.Collections.Generic.HashSet[string]]::new(
      [System.StringComparer]::Ordinal)
    foreach ($provenanceFile in $provenanceFiles) {
      $relativePath = [string] $provenanceFile.path
      if ([string]::IsNullOrWhiteSpace($relativePath) -or
          [System.IO.Path]::IsPathRooted($relativePath) -or
          $relativePath.Replace('\', '/').Split('/') -contains '..') {
        throw "Package native closure provenance for $rid contains an invalid path '$relativePath'."
      }

      $normalizedPath = $relativePath.Replace('\', '/')
      if (-not $provenancePaths.Add($normalizedPath)) {
        throw "Package native closure provenance for $rid contains duplicate path '$normalizedPath'."
      }
      if ([string] $provenanceFile.sha256 -notmatch '^[a-f0-9]{64}$') {
        throw "Package native closure provenance for $rid contains an invalid SHA-256 for '$normalizedPath'."
      }

      $assetPath = $prefix + $normalizedPath
      $assetEntries = @($fileEntries | Where-Object { $_.FullName -ceq $assetPath })
      if ($assetEntries.Count -ne 1) {
        throw "Package native closure provenance for $rid references missing asset: $assetPath"
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

    $closureEntries = @($entries | Where-Object {
      $_.StartsWith($prefix, [System.StringComparison]::Ordinal) -and $_ -cne $provenancePath
    })
    foreach ($closureEntry in $closureEntries) {
      $relativePath = $closureEntry.Substring($prefix.Length)
      if (-not $provenancePaths.Contains($relativePath)) {
        throw "Package native closure for $rid contains an asset absent from provenance: $closureEntry"
      }
    }
    if ($closureEntries.Count -ne $provenancePaths.Count) {
      throw "Package native closure for $rid does not exactly match its provenance file list."
    }
    if (@($provenancePaths | Where-Object { $_.Contains('/') }).Count -eq 0) {
      throw "Package native closure for $rid does not preserve nested asset layout."
    }

    $expectedGemLicenseDirectories = @(
      'cgi-0.4.2'
      'mini_mime-1.1.5'
      'charlock_holmes-0.7.9'
      'zlib-3.2.3'
      'resolv-0.7.2'
    )
    $gemLicensePrefix = $prefix + 'licenses/gems/'
    $actualGemLicenseDirectories = @($entries |
      Where-Object { $_.StartsWith($gemLicensePrefix, [System.StringComparison]::Ordinal) } |
      ForEach-Object { $_.Substring($gemLicensePrefix.Length).Split('/')[0] } |
      Sort-Object -Unique)
    if (($actualGemLicenseDirectories -join '|') -cne (($expectedGemLicenseDirectories | Sort-Object) -join '|')) {
      throw "Package native closure for $rid does not contain exactly the configured gem license directories."
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

  $metadata = $nuspec.SelectSingleNode(
    "/*[local-name()='package']/*[local-name()='metadata']")
  $expectedMetadata = [ordered]@{
    id = 'MBW.GHLinguist'
    title = 'MBW.GHLinguist'
    authors = 'LordMike'
    description = 'GitHub Linguist interop for .NET.'
    projectUrl = 'https://github.com/LordMike/MBW.GHLinguist'
    icon = 'icon.png'
    readme = 'README.md'
    releaseNotes = 'See the GitHub release for changes in this version.'
    tags = 'github linguist language-detection code-analysis dotnet native'
  }
  foreach ($name in $expectedMetadata.Keys) {
    $node = $metadata.SelectSingleNode("*[local-name()='$name']")
    $actualValue = if ($node) { $node.InnerText } else { '<missing>' }
    if ($actualValue -cne $expectedMetadata[$name]) {
      throw "Expected nuspec $name '$($expectedMetadata[$name])', found '$actualValue'."
    }
  }

  $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
  if (-not $licenseNode -or $licenseNode.GetAttribute('type') -cne 'expression' -or $licenseNode.InnerText -cne 'MIT') {
    throw 'Package nuspec must declare the MIT license expression.'
  }

  $repositoryNode = $metadata.SelectSingleNode("*[local-name()='repository']")
  if (-not $repositoryNode -or
      $repositoryNode.GetAttribute('type') -cne 'git' -or
      $repositoryNode.GetAttribute('url') -cne 'https://github.com/LordMike/MBW.GHLinguist' -or
      $repositoryNode.GetAttribute('commit') -notmatch '^[a-f0-9]{40}$') {
    throw 'Package nuspec must identify the exact source repository and commit.'
  }

  $dependencyGroups = @($metadata.SelectNodes(
    "*[local-name()='dependencies']/*[local-name()='group']"))
  if ($dependencyGroups.Count -ne 1 -or
      $dependencyGroups[0].GetAttribute('targetFramework') -cne 'net10.0' -or
      $dependencyGroups[0].SelectNodes("*[local-name()='dependency']").Count -ne 0) {
    throw 'Package nuspec must contain one dependency-free net10.0 group.'
  }
}
finally {
  $archive.Dispose()
}

Write-Host "Validated NuGet package: $packagePath"
