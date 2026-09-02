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
    'runtimes/linux-x64/native/ghlinguist.so'
    'runtimes/win-x64/native/ghlinguist.dll'
  )

  foreach ($requiredEntry in $requiredEntries) {
    if ($requiredEntry -notin $entries) {
      throw "Package is missing required entry: $requiredEntry"
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
