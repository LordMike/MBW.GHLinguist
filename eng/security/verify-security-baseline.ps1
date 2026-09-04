[CmdletBinding()]
param(
  [string] $ManifestPath,
  [string] $BaselinePath,
  [string] $SolutionPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ManifestPath) { $ManifestPath = Join-Path $repoRoot 'eng/linguist/native-dependencies.json' }
if (-not $BaselinePath) { $BaselinePath = Join-Path $PSScriptRoot 'security-baseline.json' }
if (-not $SolutionPath) { $SolutionPath = Join-Path $repoRoot 'MBW.GHLinguist.slnx' }

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
if ($baseline.schemaVersion -ne 1 -or @($baseline.nativeMinimumVersions).Count -eq 0) {
  throw 'Security baseline has an unsupported or empty schema.'
}

foreach ($requirement in $baseline.nativeMinimumVersions) {
  $actualVersion = switch ($requirement.source) {
    'ruby' { [string] $manifest.ruby.version }
    'gem' {
      $matches = @($manifest.gems | Where-Object { $_.name -ceq $requirement.name })
      if ($matches.Count -ne 1) {
        throw "Security baseline expected exactly one configured gem named '$($requirement.name)'."
      }
      [string] $matches[0].version
    }
    default { throw "Security baseline contains unsupported source '$($requirement.source)'." }
  }

  if ([version] $actualVersion -lt [version] $requirement.minimumVersion) {
    throw "$($requirement.component) $actualVersion is below the secure minimum $($requirement.minimumVersion) for $($requirement.cve). See $($requirement.advisory)"
  }
}

$auditOutput = & dotnet package list --project $SolutionPath --vulnerable --include-transitive --format json --output-version 1
if ($LASTEXITCODE -ne 0) {
  throw "The NuGet vulnerability audit failed: $($auditOutput -join [Environment]::NewLine)"
}
$audit = ($auditOutput -join [Environment]::NewLine) | ConvertFrom-Json
$vulnerablePackages = [System.Collections.Generic.List[string]]::new()
foreach ($project in @($audit.projects)) {
  $frameworksProperty = $project.PSObject.Properties['frameworks']
  if (-not $frameworksProperty) { continue }
  foreach ($framework in @($frameworksProperty.Value)) {
    foreach ($collectionName in 'topLevelPackages', 'transitivePackages') {
      $collectionProperty = $framework.PSObject.Properties[$collectionName]
      if (-not $collectionProperty) { continue }
      foreach ($package in @($collectionProperty.Value)) {
        if (@($package.vulnerabilities).Count -gt 0) {
          $vulnerablePackages.Add("$($project.path): $($package.id) $($package.resolvedVersion)")
        }
      }
    }
  }
}
if ($vulnerablePackages.Count -gt 0) {
  throw "NuGet vulnerability audit found known vulnerabilities:$([Environment]::NewLine)$($vulnerablePackages -join [Environment]::NewLine)"
}

Write-Host "Validated $(@($baseline.nativeMinimumVersions).Count) native security minimums and found no vulnerable NuGet packages."
