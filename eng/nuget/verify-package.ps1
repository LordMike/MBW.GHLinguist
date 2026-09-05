[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string] $Package,

  [Parameter(Mandatory)]
  [string] $ExpectedVersion,

  [Parameter(Mandatory)]
  [ValidatePattern('^[a-fA-F0-9]{40}$')]
  [string] $ExpectedCommit,

  [Parameter(Mandatory)]
  [ValidateSet('managed', 'runtime')]
  [string] $Kind,

  [ValidateSet('win-x64', 'linux-x64')]
  [string] $RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (($Kind -eq 'runtime') -ne (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier))) {
  throw 'RuntimeIdentifier is required only when validating a runtime package.'
}

$packageMatches = @(Resolve-Path -Path $Package)
if ($packageMatches.Count -ne 1) {
  throw "Expected exactly one NuGet package matching '$Package', found $($packageMatches.Count)."
}

$packagePath = $packageMatches[0].Path
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$inventoryPath = Join-Path $repoRoot 'eng/linguist/third-party-redistribution.json'
$commonEntries = @(
  'README.md'
  'LICENSE'
  'THIRD-PARTY-NOTICES.md'
  'THIRD-PARTY-REDISTRIBUTION.json'
  'icon.png'
)

function Read-EntryText {
  param([Parameter(Mandatory)] $Entry)

  $reader = [System.IO.StreamReader]::new($Entry.Open())
  try {
    return $reader.ReadToEnd()
  }
  finally {
    $reader.Dispose()
  }
}

function Get-EntrySha256 {
  param([Parameter(Mandatory)] $Entry)

  $stream = $Entry.Open()
  $hash = [System.Security.Cryptography.SHA256]::Create()
  try {
    return [System.Convert]::ToHexString($hash.ComputeHash($stream)).ToLowerInvariant()
  }
  finally {
    $hash.Dispose()
    $stream.Dispose()
  }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
  $fileEntries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
  $entries = @($fileEntries.FullName)
  $entriesByName = @{}
  foreach ($entry in $fileEntries) {
    $segments = $entry.FullName.Split('/')
    if ([string]::IsNullOrWhiteSpace($entry.FullName) -or
        $entry.FullName.Contains('\') -or
        $entry.FullName.StartsWith('/') -or
        $entry.FullName -match '^[A-Za-z]:' -or
        $segments -contains '' -or
        $segments -contains '.' -or
        $segments -contains '..') {
      throw "Package contains an invalid entry path: $($entry.FullName)"
    }
    if ($entriesByName.ContainsKey($entry.FullName)) {
      throw "Package contains duplicate or case-colliding entry paths: $($entry.FullName)"
    }
    $entriesByName[$entry.FullName] = $entry
  }

  foreach ($requiredEntry in $commonEntries) {
    if (-not $entriesByName.ContainsKey($requiredEntry)) {
      throw "Package is missing required entry: $requiredEntry"
    }
  }

  $sourceInventoryText = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $inventoryPath).Path)
  $packagedInventoryText = Read-EntryText $entriesByName['THIRD-PARTY-REDISTRIBUTION.json']
  if ($packagedInventoryText -cne $sourceInventoryText) {
    throw 'Package third-party redistribution inventory does not match the checked-out source.'
  }
  $redistributionInventory = $packagedInventoryText | ConvertFrom-Json
  if ($redistributionInventory.schemaVersion -ne 4 -or @($redistributionInventory.components).Count -eq 0) {
    throw 'Package third-party redistribution inventory has an unsupported or empty schema.'
  }

  $allowedEntries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($entry in @(
    $commonEntries
    'MBW.GHLinguist.nuspec'
    'MBW.GHLinguist.Runtime.win-x64.nuspec'
    'MBW.GHLinguist.Runtime.linux-x64.nuspec'
    '_rels/.rels'
    '[Content_Types].xml'
    'package/services/metadata/core-properties/nuget.psmdcp'
  )) {
    [void] $allowedEntries.Add($entry)
  }

  if ($Kind -eq 'managed') {
    foreach ($entry in @(
      'lib/net10.0/MBW.GHLinguist.dll'
      'lib/net10.0/MBW.GHLinguist.xml'
      'native/include/ghlinguist.h'
      'buildTransitive/MBW.GHLinguist.targets'
    )) {
      if (-not $entriesByName.ContainsKey($entry)) {
        throw "Managed package is missing required entry: $entry"
      }
      [void] $allowedEntries.Add($entry)
    }
    foreach ($entry in $entries) {
      if ($entry.StartsWith('nativeassets/', [System.StringComparison]::Ordinal) -or
          $entry.StartsWith('runtimes/', [System.StringComparison]::Ordinal)) {
        throw "Managed package must not contain runtime closure assets: $entry"
      }
      if (-not $allowedEntries.Contains($entry)) {
        throw "Managed package contains an unexpected entry: $entry"
      }
    }
  }
  else {
    if (-not $entriesByName.ContainsKey('PACKAGE-LICENSES.md')) {
      throw 'Runtime package is missing required entry: PACKAGE-LICENSES.md'
    }
    [void] $allowedEntries.Add('PACKAGE-LICENSES.md')

    $bridgeName = if ($RuntimeIdentifier -eq 'win-x64') { 'ghlinguist.dll' } else { 'ghlinguist.so' }
    $closurePrefix = "nativeassets/$RuntimeIdentifier/"
    foreach ($entry in @(
      ($closurePrefix + $bridgeName)
      "buildTransitive/MBW.GHLinguist.Runtime.$RuntimeIdentifier.targets"
      ($closurePrefix + 'provenance.json')
    )) {
      if (-not $entriesByName.ContainsKey($entry)) {
        throw "Runtime package is missing required entry: $entry"
      }
      [void] $allowedEntries.Add($entry)
    }

    foreach ($entry in $entries) {
      if ($entry.StartsWith($closurePrefix, [System.StringComparison]::Ordinal) -or $allowedEntries.Contains($entry)) {
        continue
      }
      throw "Runtime package contains an unexpected entry: $entry"
    }
    if (@($entries | Where-Object { $_.StartsWith('lib/', [System.StringComparison]::Ordinal) }).Count -ne 0) {
      throw 'Runtime packages must not contain managed compile assets.'
    }

    foreach ($component in $redistributionInventory.components) {
      $requiredOutputProperty = $component.requiredOutputs.PSObject.Properties[$RuntimeIdentifier]
      if (-not $requiredOutputProperty) {
        continue
      }
      foreach ($relativeOutput in @($requiredOutputProperty.Value)) {
        if ([string]::IsNullOrWhiteSpace($relativeOutput) -or
            $relativeOutput.Contains('\') -or
            $relativeOutput.StartsWith('/') -or
            $relativeOutput.Split('/') -contains '..') {
          throw "Redistribution component '$($component.component)' declares invalid output '$relativeOutput'."
        }
        $packageOutput = $closurePrefix + $relativeOutput
        if (-not $entriesByName.ContainsKey($packageOutput)) {
          throw "Runtime package is missing declared license output for '$($component.component)': $packageOutput"
        }
      }
    }

    $provenancePath = $closurePrefix + 'provenance.json'
    $provenance = (Read-EntryText $entriesByName[$provenancePath]) | ConvertFrom-Json
    if ($provenance.platform -ne $RuntimeIdentifier -or $provenance.schemaVersion -ne 2) {
      throw "Runtime package provenance does not describe $RuntimeIdentifier using schema version 2."
    }

    $provenanceFiles = @($provenance.files)
    if ($provenanceFiles.Count -eq 0) {
      throw "Runtime package provenance for $RuntimeIdentifier does not describe any native assets."
    }

    $provenancePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($provenanceFile in $provenanceFiles) {
      $relativePath = [string] $provenanceFile.path
      if ([string]::IsNullOrWhiteSpace($relativePath) -or
          [System.IO.Path]::IsPathRooted($relativePath) -or
          $relativePath.Replace('\', '/').Split('/') -contains '..' -or
          [string] $provenanceFile.sha256 -notmatch '^[a-f0-9]{64}$') {
        throw "Runtime package provenance contains an invalid file entry: $relativePath"
      }
      $normalizedPath = $relativePath.Replace('\', '/')
      if (-not $provenancePaths.Add($normalizedPath)) {
        throw "Runtime package provenance contains duplicate path '$normalizedPath'."
      }
      $assetPath = $closurePrefix + $normalizedPath
      if (-not $entriesByName.ContainsKey($assetPath)) {
        throw "Runtime package provenance references missing asset: $assetPath"
      }
      if ((Get-EntrySha256 $entriesByName[$assetPath]) -ne $provenanceFile.sha256) {
        throw "Runtime package native asset hash does not match provenance: $assetPath"
      }
    }

    $closureEntries = @($entries | Where-Object { $_.StartsWith($closurePrefix, [System.StringComparison]::Ordinal) -and $_ -cne $provenancePath })
    if ($closureEntries.Count -ne $provenancePaths.Count) {
      throw 'Runtime package native closure does not exactly match its provenance file list.'
    }
    foreach ($closureEntry in $closureEntries) {
      if (-not $provenancePaths.Contains($closureEntry.Substring($closurePrefix.Length))) {
        throw "Runtime package closure contains an asset absent from provenance: $closureEntry"
      }
    }
  }

  $nuspecEntries = @($fileEntries | Where-Object { $_.FullName -like '*.nuspec' })
  if ($nuspecEntries.Count -ne 1) {
    throw "Expected exactly one nuspec entry, found $($nuspecEntries.Count)."
  }
  [xml] $nuspec = Read-EntryText $nuspecEntries[0]
  $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
  $expectedMetadata = if ($Kind -eq 'managed') {
    [ordered]@{
      id = 'MBW.GHLinguist'
      title = 'MBW.GHLinguist'
      description = 'GitHub Linguist interop for .NET 10. Set RuntimeIdentifier to win-x64 or linux-x64; matching native assets are selected automatically.'
      tags = 'github linguist language-detection code-analysis dotnet native'
    }
  }
  else {
    [ordered]@{
      id = "MBW.GHLinguist.Runtime.$RuntimeIdentifier"
      title = "MBW.GHLinguist.Runtime.$RuntimeIdentifier"
      description = "$(if ($RuntimeIdentifier -eq 'win-x64') { 'Windows' } else { 'Linux' }) x64 native runtime closure for MBW.GHLinguist. Installed transitively; reference MBW.GHLinguist instead."
      tags = "github linguist language-detection code-analysis dotnet native $RuntimeIdentifier"
    }
  }
  $expectedMetadata['authors'] = 'LordMike'
  $expectedMetadata['projectUrl'] = 'https://github.com/LordMike/MBW.GHLinguist'
  $expectedMetadata['icon'] = 'icon.png'
  $expectedMetadata['readme'] = 'README.md'
  $expectedMetadata['releaseNotes'] = 'See the GitHub release for changes in this version.'
  foreach ($name in $expectedMetadata.Keys) {
    $node = $metadata.SelectSingleNode("*[local-name()='$name']")
    $actualValue = if ($node) { $node.InnerText } else { '<missing>' }
    if ($actualValue -cne $expectedMetadata[$name]) {
      throw "Expected nuspec $name '$($expectedMetadata[$name])', found '$actualValue'."
    }
  }

  $versionNode = $metadata.SelectSingleNode("*[local-name()='version']")
  if (-not $versionNode -or $versionNode.InnerText -ne $ExpectedVersion) {
    throw "Expected package version '$ExpectedVersion', found '$(if ($versionNode) { $versionNode.InnerText } else { '<missing>' })'."
  }
  $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
  if ($Kind -eq 'managed') {
    if (-not $licenseNode -or $licenseNode.GetAttribute('type') -cne 'expression' -or $licenseNode.InnerText -cne 'MIT') {
      throw 'Managed package nuspec must declare the MIT license expression.'
    }
  }
  elseif (-not $licenseNode -or $licenseNode.GetAttribute('type') -cne 'file' -or $licenseNode.InnerText -cne 'PACKAGE-LICENSES.md') {
    throw 'Runtime package nuspec must reference PACKAGE-LICENSES.md.'
  }
  $repositoryNode = $metadata.SelectSingleNode("*[local-name()='repository']")
  if (-not $repositoryNode -or
      $repositoryNode.GetAttribute('type') -cne 'git' -or
      $repositoryNode.GetAttribute('url') -cne 'https://github.com/LordMike/MBW.GHLinguist' -or
      $repositoryNode.GetAttribute('commit') -cne $ExpectedCommit.ToLowerInvariant()) {
    throw 'Package nuspec must identify the exact source repository and commit.'
  }

  $dependencyGroups = @($metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']"))
  if ($dependencyGroups.Count -ne 1 -or $dependencyGroups[0].GetAttribute('targetFramework') -cne 'net10.0') {
    throw 'Package nuspec must contain exactly one net10.0 dependency group.'
  }
  $dependencies = @($dependencyGroups[0].SelectNodes("*[local-name()='dependency']"))
  if ($Kind -eq 'managed') {
    $expectedRuntimeDependencies = @(
      'MBW.GHLinguist.Runtime.linux-x64'
      'MBW.GHLinguist.Runtime.win-x64'
    )
    if ($dependencies.Count -ne $expectedRuntimeDependencies.Count) {
      throw 'Managed package must declare both runtime package dependencies.'
    }
    foreach ($expectedRuntimeDependency in $expectedRuntimeDependencies) {
      $matches = @($dependencies | Where-Object {
        $_.GetAttribute('id') -ceq $expectedRuntimeDependency -and
        $_.GetAttribute('version') -ceq "[$ExpectedVersion]"
      })
      if ($matches.Count -ne 1) {
        throw "Managed package must declare exact dependency $expectedRuntimeDependency [$ExpectedVersion]."
      }
    }
  }
  elseif ($dependencies.Count -ne 0) {
    throw 'Runtime packages must not declare package dependencies.'
  }
}
finally {
  $archive.Dispose()
}

Write-Host "Validated $Kind NuGet package: $packagePath"
