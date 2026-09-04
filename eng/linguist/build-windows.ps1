[CmdletBinding()]
param(
  [string] $RubyRoot,
  [string] $LinguistRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../..')).Path
$manifestPath = Join-Path $scriptRoot 'native-dependencies.json'
$licenseInventoryPath = Join-Path $scriptRoot 'third-party-redistribution.json'
$nativeAssetRoot = Join-Path $repoRoot '.tmp/artifacts/native/win-x64'
$buildRoot = Join-Path $repoRoot '.tmp/build/linguist/win-x64'

function Invoke-Checked {
  param(
    [Parameter(Mandatory)] [string] $Command,
    [Parameter(ValueFromRemainingArguments)] [string[]] $Arguments
  )

  & $Command @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed with exit code ${LASTEXITCODE}: $Command $Arguments"
  }
}

function Require-Path {
  param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $Description)
  if (-not (Test-Path -LiteralPath $Path)) {
    throw "Required $Description is missing: $Path"
  }
}

function Get-NormalizedTextSha256 {
  param([Parameter(Mandatory)] [string] $Path)

  $text = [System.IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n")
  $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($text)
  return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Copy-RequiredDirectory {
  param([Parameter(Mandatory)] [string] $Source, [Parameter(Mandatory)] [string] $Destination, [Parameter(Mandatory)] [string] $Description)
  Require-Path $Source $Description
  New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
  Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

function Copy-RequiredDirectoryContents {
  param([Parameter(Mandatory)] [string] $Source, [Parameter(Mandatory)] [string] $Destination, [Parameter(Mandatory)] [string] $Description)
  Require-Path $Source $Description
  New-Item -ItemType Directory -Path $Destination -Force | Out-Null
  Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function Copy-FirstRequiredLicense {
  param([Parameter(Mandatory)] [string[]] $Sources, [Parameter(Mandatory)] [string] $Destination, [Parameter(Mandatory)] [string] $Description)

  $source = $Sources | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
  if (-not $source) {
    throw "Required $Description license text is missing. Expected one of: $($Sources -join '; ')"
  }
  New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
  Copy-Item -LiteralPath $source -Destination $Destination -Force
}

function Copy-LockedRemoteFile {
  param(
    [Parameter(Mandatory)] [string] $Destination,
    [Parameter(Mandatory)] [string] $Url,
    [Parameter(Mandatory)] [string] $Sha256
  )

  if ($Sha256 -notmatch '^[a-f0-9]{64}$') {
    throw "Invalid SHA-256 for locked file $Url."
  }
  New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
  Invoke-WebRequest -Uri $Url -OutFile $Destination
  $actualSha256 = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualSha256 -ne $Sha256) {
    throw "SHA-256 mismatch for locked file $Url."
  }
}

function Copy-RequiredGemLicenses {
  param([Parameter(Mandatory)] [string] $GemRoot, [Parameter(Mandatory)] [string] $Destination, [Parameter(Mandatory)] [string] $Description, [string[]] $FallbackSources = @())

  $licenses = @(Get-ChildItem -LiteralPath $GemRoot -File -Recurse | Where-Object { $_.Name -match '^(?i:license|copying)' })
  if ($licenses.Count -eq 0) {
    $licenses = @($FallbackSources |
      Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
      ForEach-Object { Get-Item -LiteralPath $_ })
  }
  if ($licenses.Count -eq 0) {
    throw "Required license or copying file is missing from $Description."
  }
  New-Item -ItemType Directory -Path $Destination -Force | Out-Null
  foreach ($license in $licenses) {
    Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $Destination $license.Name) -Force
  }
}

function Write-Provenance {
  param([Parameter(Mandatory)] $Manifest, [Parameter(Mandatory)] [string] $Root, [Parameter(Mandatory)] [object[]] $PacmanPackages, [Parameter(Mandatory)] [string] $RubyDescription)

  $files = Get-ChildItem -LiteralPath $Root -File -Recurse |
    Where-Object { $_.Name -ne 'provenance.json' } |
    Sort-Object FullName |
    ForEach-Object {
      [ordered]@{
        path = $_.FullName.Substring($Root.Length).TrimStart('\', '/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      }
    }

  [ordered]@{
    schemaVersion = 2
    platform = 'win-x64'
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    lockInputs = [ordered]@{
      nativeDependenciesSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
      thirdPartyRedistributionSha256 = (Get-FileHash -LiteralPath $licenseInventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
      bridgeSha256 = Get-NormalizedTextSha256 (Join-Path $repoRoot 'src/MBW.GHLinguist.Native/ruby/ghlinguist/bridge.rb')
      linguistVersionSha256 = Get-NormalizedTextSha256 (Join-Path $LinguistRoot 'lib/linguist/VERSION')
    }
    externalDependencies = [ordered]@{
      ruby = [ordered]@{
        version = $Manifest.ruby.version
        description = $RubyDescription
        artifact = $Manifest.ruby.windowsArtifact
        bundledComponents = @($Manifest.ruby.bundledComponents)
      }
      gems = @($Manifest.gems | ForEach-Object {
        [ordered]@{
          name = $_.name
          version = $_.version
          artifact = $_.artifact
          artifactUrl = $_.artifactUrl
          sha256 = $_.sha256
          windowsBuildArguments = @($_.windowsBuildArguments)
        }
      })
      pacmanPackages = @($PacmanPackages)
    }
    rubyVersion = $Manifest.ruby.version
    linguistVersion = $Manifest.linguist.version
    linguistRevision = $Manifest.linguist.revision
    classifierSha256 = $Manifest.linguist.classifierSha256
    buildConfiguration = $Manifest.build
    files = @($files)
  } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Root 'provenance.json') -Encoding utf8NoBOM
}

foreach ($command in 'git', 'cmake') {
  if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
    throw "Required command is unavailable: $command"
  }
}

Require-Path $manifestPath 'native dependency manifest'
Require-Path $licenseInventoryPath 'third-party redistribution inventory'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if (-not $RubyRoot) {
  if (-not (Get-Command ruby -ErrorAction SilentlyContinue)) {
    throw 'RubyRoot was not provided and ruby is unavailable on PATH.'
  }
  $RubyRoot = (& ruby -rrbconfig -e 'print RbConfig::CONFIG["prefix"]').Trim()
}
$RubyRoot = (Resolve-Path -LiteralPath $RubyRoot).Path
$ruby = Join-Path $RubyRoot 'bin/ruby.exe'
Require-Path $ruby 'RubyInstaller executable'

if (-not $LinguistRoot) {
  $LinguistRoot = Join-Path $repoRoot 'extern/linguist'
}
$LinguistRoot = (Resolve-Path -LiteralPath $LinguistRoot).Path
Require-Path (Join-Path $LinguistRoot 'ext/linguist/extconf.rb') 'Linguist tokenizer source'

$actualRubyVersion = (& $ruby -e 'print RUBY_VERSION').Trim()
if ($actualRubyVersion -ne $manifest.ruby.version) {
  throw "Expected Ruby $($manifest.ruby.version), found $actualRubyVersion."
}
$actualLinguistRevision = (& git -C $LinguistRoot rev-parse HEAD).Trim()
if ($actualLinguistRevision -ne $manifest.linguist.revision) {
  throw "Expected Linguist revision $($manifest.linguist.revision), found $actualLinguistRevision."
}
$actualLinguistVersion = (Get-Content -LiteralPath (Join-Path $LinguistRoot 'lib/linguist/VERSION') -Raw).Trim()
if ($actualLinguistVersion -ne $manifest.linguist.version) {
  throw "Expected Linguist $($manifest.linguist.version), found $actualLinguistVersion."
}

$bridgeSource = Join-Path $repoRoot 'src/MBW.GHLinguist.Native/ruby/ghlinguist/bridge.rb'
Require-Path $bridgeSource 'GHLinguist Ruby bridge'

$msysBin = Join-Path $RubyRoot 'msys64/ucrt64/bin'
if (-not (Get-Command make -ErrorAction SilentlyContinue) -or -not (Get-Command gcc -ErrorAction SilentlyContinue)) {
  Require-Path $msysBin 'RubyInstaller MSYS2 UCRT toolchain'
  $env:Path = "$msysBin$([IO.Path]::PathSeparator)$env:Path"
}
foreach ($command in 'make', 'gcc') {
  if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
    throw "Required RubyInstaller Devkit command is unavailable: $command"
  }
}

$pacman = Join-Path $RubyRoot 'msys64/usr/bin/pacman.exe'
Require-Path $pacman 'RubyInstaller MSYS2 package manager'
$pacmanPackages = foreach ($package in $manifest.windows.pacmanPackages) {
  if ($package -notmatch '^[a-z0-9][a-z0-9+._-]*$') {
    throw "Invalid pacman package allowlist entry: $package"
  }
  $identity = (& $pacman '-Q' $package).Trim()
  if ($LASTEXITCODE -ne 0 -or -not $identity) {
    throw "Required allowlisted pacman package is not installed: $package"
  }
  $parts = $identity -split '\s+', 2
  if ($parts.Count -ne 2) {
    throw "Unable to parse pacman package identity: $identity"
  }
  $expectedVersion = $manifest.windows.pacmanPackageVersions.PSObject.Properties[$package].Value
  if (-not $expectedVersion -or $parts[1] -ne $expectedVersion) {
    throw "Expected pacman package $package $expectedVersion, found $($parts[1])."
  }
  [ordered]@{ name = $parts[0]; version = $parts[1] }
}
$rubyDescription = (& $ruby -e 'print RUBY_DESCRIPTION').Trim()
if ($LASTEXITCODE -ne 0 -or -not $rubyDescription) {
  throw 'Unable to identify the pinned Ruby runtime.'
}

Remove-Item -LiteralPath $buildRoot, $nativeAssetRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $buildRoot, $nativeAssetRoot -Force | Out-Null

$runtimeFiles = @($manifest.nativeRuntime.'win-x64')
if ($manifest.schemaVersion -ne 6 -or $runtimeFiles.Count -eq 0) {
  throw 'The Windows native runtime allowlist is missing or uses an unsupported schema.'
}
foreach ($runtimeFile in $runtimeFiles) {
  if ([string]::IsNullOrWhiteSpace($runtimeFile.source) -or
      $runtimeFile.source.Contains('\') -or
      $runtimeFile.source.Split('/') -contains '..' -or
      @($runtimeFile.destinations).Count -eq 0 -or
      $runtimeFile.sha256 -notmatch '^[a-f0-9]{64}$') {
    throw "Invalid Windows native runtime allowlist entry: $($runtimeFile | ConvertTo-Json -Compress)"
  }

  $sourcePath = Join-Path $RubyRoot $runtimeFile.source
  Require-Path $sourcePath "allowlisted native runtime source '$($runtimeFile.source)'"
  $sourceSha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($sourceSha256 -ne $runtimeFile.sha256) {
    throw "SHA-256 mismatch for allowlisted native runtime source '$($runtimeFile.source)'."
  }

  foreach ($destination in $runtimeFile.destinations) {
    if ([string]::IsNullOrWhiteSpace($destination) -or
        $destination.Contains('\') -or
        $destination.Split('/') -contains '..') {
      throw "Invalid Windows native runtime destination '$destination'."
    }
    $destinationPath = Join-Path $nativeAssetRoot $destination
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
  }
}
Copy-RequiredDirectory (Join-Path $RubyRoot 'lib/ruby') (Join-Path $nativeAssetRoot 'lib/ruby') 'Ruby standard library'

$gemHome = Join-Path $nativeAssetRoot "lib/ruby/gems/$($manifest.ruby.abiVersion)"
$stagedGemRoot = Join-Path $nativeAssetRoot 'lib/ruby/gems'
Remove-Item -LiteralPath $stagedGemRoot -Recurse -Force -ErrorAction SilentlyContinue
$sourceGemHome = (& $ruby -rrubygems -e 'print Gem.dir').Trim()
New-Item -ItemType Directory -Path (Join-Path $gemHome 'gems'), (Join-Path $gemHome 'specifications') -Force | Out-Null
foreach ($gem in $manifest.gems) {
  $gemLocation = (& $ruby -rrubygems -e "spec = Gem::Specification.find_by_name('$($gem.name)', '=$($gem.version)'); print spec.full_gem_path").Trim()
  if ($LASTEXITCODE -ne 0 -or -not $gemLocation) {
    throw "Required gem $($gem.name) $($gem.version) is not installed in $RubyRoot. Install the pinned gem before staging."
  }
  $gemSpec = (& $ruby -rrubygems -e "spec = Gem::Specification.find_by_name('$($gem.name)', '=$($gem.version)'); print spec.spec_file").Trim()
  Copy-RequiredDirectory $gemLocation (Join-Path $gemHome "gems/$($gem.name)-$($gem.version)") "gem $($gem.name) $($gem.version)"
  Require-Path $gemSpec "gem specification for $($gem.name) $($gem.version)"
  Copy-Item -LiteralPath $gemSpec -Destination (Join-Path $gemHome "specifications/$($gem.name)-$($gem.version).gemspec") -Force
  $gemExtensionDir = & $ruby -rrubygems -e "spec = Gem::Specification.find_by_name('$($gem.name)', '=$($gem.version)'); print spec.extensions.empty? ? '' : spec.extension_dir"
  $gemExtensionDir = if ($null -eq $gemExtensionDir) { '' } else { $gemExtensionDir.Trim() }
  if ($gemExtensionDir) {
    Require-Path $gemExtensionDir "native extension for gem $($gem.name) $($gem.version)"
    $gemExtensionRelativePath = $gemExtensionDir.Substring($sourceGemHome.Length).TrimStart('\', '/')
    Copy-RequiredDirectory $gemExtensionDir (Join-Path $gemHome $gemExtensionRelativePath) "native extension for gem $($gem.name) $($gem.version)"
  }
  $gemLicenseFallbacks = if ($gem.name -eq 'charlock_holmes' -and $gem.version -eq '0.7.9') {
    @((Join-Path $scriptRoot 'licenses/charlock_holmes-0.7.9-LICENSE'))
  }
  else {
    @()
  }
  Copy-RequiredGemLicenses $gemLocation (Join-Path $nativeAssetRoot "licenses/gems/$($gem.name)-$($gem.version)") "gem $($gem.name) $($gem.version)" $gemLicenseFallbacks
}
Get-ChildItem -LiteralPath (Join-Path $gemHome 'gems') -Directory | ForEach-Object {
  Remove-Item -LiteralPath (Join-Path $_.FullName 'ext') -Recurse -Force -ErrorAction SilentlyContinue
}
$expectedGems = @($manifest.gems | ForEach-Object { "$($_.name)-$($_.version)" } | Sort-Object)
$actualGems = @(Get-ChildItem -LiteralPath (Join-Path $gemHome 'gems') -Directory | ForEach-Object Name | Sort-Object)
if (($actualGems -join '|') -ne ($expectedGems -join '|')) {
  throw "Unexpected staged gems: $($actualGems -join ', '); expected $($expectedGems -join ', ')."
}

Copy-FirstRequiredLicense -Sources @((Join-Path $repoRoot 'LICENSE')) -Destination (Join-Path $nativeAssetRoot 'licenses/MBW.GHLinguist/LICENSE') -Description 'MBW.GHLinguist'
Copy-FirstRequiredLicense -Sources @((Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md')) -Destination (Join-Path $nativeAssetRoot 'licenses/MBW.GHLinguist/THIRD-PARTY-NOTICES.md') -Description 'MBW.GHLinguist notices'
foreach ($licenseFile in $manifest.ruby.licenseFiles) {
  Copy-LockedRemoteFile -Destination (Join-Path $nativeAssetRoot "licenses/ruby/$($licenseFile.name)") -Url $licenseFile.url -Sha256 $licenseFile.sha256
}
Copy-FirstRequiredLicense -Sources @((Join-Path $RubyRoot 'LICENSE'), (Join-Path $RubyRoot 'LICENSE.txt'), (Join-Path $RubyRoot 'RubyInstaller-LICENSE.txt')) -Destination (Join-Path $nativeAssetRoot 'licenses/rubyinstaller/LICENSE') -Description 'RubyInstaller'
$icuLicenseSources = @(
  (Join-Path $RubyRoot 'msys64/ucrt64/share/licenses/icu/LICENSE')
  (Join-Path $RubyRoot 'msys64/ucrt64/share/licenses/icu/LICENSE.txt')
  Get-ChildItem -Path (Join-Path $RubyRoot 'msys64/ucrt64/share/icu/*/LICENSE') -File -ErrorAction SilentlyContinue |
    ForEach-Object FullName
)
Copy-FirstRequiredLicense -Sources $icuLicenseSources -Destination (Join-Path $nativeAssetRoot 'licenses/msys2/icu/LICENSE') -Description 'ICU'
Copy-FirstRequiredLicense -Sources @((Join-Path $RubyRoot 'msys64/ucrt64/share/licenses/gcc-libs/COPYING3')) -Destination (Join-Path $nativeAssetRoot 'licenses/msys2/gcc/COPYING3') -Description 'GCC'
Copy-FirstRequiredLicense -Sources @((Join-Path $RubyRoot 'msys64/ucrt64/share/licenses/gcc-libs/COPYING.RUNTIME')) -Destination (Join-Path $nativeAssetRoot 'licenses/msys2/gcc/COPYING.RUNTIME') -Description 'GCC runtime exception'
Copy-FirstRequiredLicense -Sources @((Join-Path $RubyRoot 'msys64/ucrt64/share/licenses/winpthreads/COPYING')) -Destination (Join-Path $nativeAssetRoot 'licenses/msys2/winpthreads/COPYING') -Description 'winpthreads'

foreach ($path in $manifest.linguist.paths) {
  if ($path -eq 'lib') {
    Copy-RequiredDirectoryContents (Join-Path $LinguistRoot $path) (Join-Path $nativeAssetRoot 'lib') "Linguist $path"
  }
  else {
    Copy-RequiredDirectory (Join-Path $LinguistRoot $path) (Join-Path $nativeAssetRoot "linguist/$path") "Linguist $path"
  }
}
Copy-FirstRequiredLicense -Sources @((Join-Path $LinguistRoot 'LICENSE')) -Destination (Join-Path $nativeAssetRoot 'licenses/linguist/LICENSE') -Description 'GitHub Linguist'
New-Item -ItemType Directory -Path (Join-Path $nativeAssetRoot 'ghlinguist') -Force | Out-Null
Copy-Item -LiteralPath $bridgeSource -Destination (Join-Path $nativeAssetRoot 'ghlinguist/bridge.rb') -Force
$extensionSource = Join-Path $buildRoot 'tokenizer'
Copy-RequiredDirectory (Join-Path $LinguistRoot $manifest.linguist.tokenizerExtension) $extensionSource 'Linguist tokenizer source'
Push-Location $extensionSource
try {
  Invoke-Checked $ruby 'extconf.rb'
  Invoke-Checked make '-j2'
}
finally {
  Pop-Location
}
$tokenizer = Get-ChildItem -LiteralPath $extensionSource -File | Where-Object { $_.Name -in @('linguist.dll', 'linguist.so') } | Select-Object -First 1
if (-not $tokenizer) {
  throw 'Linguist tokenizer build completed without producing linguist.dll or linguist.so.'
}
New-Item -ItemType Directory -Path (Join-Path $nativeAssetRoot 'lib/linguist') -Force | Out-Null
Copy-Item -LiteralPath $tokenizer.FullName -Destination (Join-Path $nativeAssetRoot "lib/linguist/$($tokenizer.Name)") -Force
Copy-RequiredDirectory (Join-Path $LinguistRoot 'samples') (Join-Path $nativeAssetRoot 'samples') 'Linguist classifier samples'
$previousRubyLibForSamples, $previousGemHomeForSamples, $previousGemPathForSamples = $env:RUBYLIB, $env:GEM_HOME, $env:GEM_PATH
try {
  $env:RUBYLIB = Join-Path $nativeAssetRoot 'lib'
  $env:GEM_HOME = $gemHome
  $env:GEM_PATH = $gemHome
  Invoke-Checked $ruby (Join-Path $scriptRoot 'generate-samples.rb') (Join-Path $nativeAssetRoot 'lib/linguist/samples_data.rb')
}
finally {
  $env:RUBYLIB = $previousRubyLibForSamples
  $env:GEM_HOME = $previousGemHomeForSamples
  $env:GEM_PATH = $previousGemPathForSamples
  Remove-Item -LiteralPath (Join-Path $nativeAssetRoot 'samples') -Recurse -Force
}
$classifierSha256 = (Get-FileHash -LiteralPath (Join-Path $nativeAssetRoot 'lib/linguist/samples_data.rb') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($classifierSha256 -ne $manifest.linguist.classifierSha256) {
  throw "Expected classifier SHA-256 $($manifest.linguist.classifierSha256), found $classifierSha256."
}

$bridgeBuild = Join-Path $buildRoot 'bridge'
Invoke-Checked cmake '-S' (Join-Path $repoRoot 'src/MBW.GHLinguist.Native') '-B' $bridgeBuild '-G' 'Ninja' `
  "-DGHL_RUBY_ROOT=$RubyRoot" '-DGHL_BUILD_SMOKE=ON' "-DGHL_SMOKE_ASSET_ROOT=$nativeAssetRoot" `
  "-DGHL_LINGUIST_REVISION=$actualLinguistRevision" "-DGHL_CLASSIFIER_SHA256=$classifierSha256"
Invoke-Checked cmake '--build' $bridgeBuild '--parallel' '2'
$bridge = Get-ChildItem -LiteralPath $bridgeBuild -Filter 'ghlinguist.dll' -File -Recurse | Select-Object -First 1
if (-not $bridge) {
  throw 'ghlinguist bridge build completed without producing ghlinguist.dll.'
}
Copy-Item -LiteralPath $bridge.FullName -Destination (Join-Path $nativeAssetRoot 'ghlinguist.dll') -Force

$smoke = Get-ChildItem -LiteralPath $bridgeBuild -Filter 'ghlinguist_smoke.exe' -File -Recurse | Select-Object -First 1
if (-not $smoke) {
  throw 'Native bridge build completed without producing ghlinguist_smoke.exe.'
}
$stagedSmoke = Join-Path $nativeAssetRoot 'ghlinguist_smoke.exe'
Copy-Item -LiteralPath $smoke.FullName -Destination $stagedSmoke -Force
$previousPath = $env:Path
try {
  $env:Path = "$nativeAssetRoot$([IO.Path]::PathSeparator)$env:Path"
  Invoke-Checked $stagedSmoke $nativeAssetRoot
}
finally {
  $env:Path = $previousPath
  Remove-Item -LiteralPath $stagedSmoke -Force -ErrorAction SilentlyContinue
}

$previousRubyLib, $previousGemHome, $previousGemPath, $previousManifest = $env:RUBYLIB, $env:GEM_HOME, $env:GEM_PATH, $env:GHL_DEPENDENCY_MANIFEST
try {
  $env:RUBYLIB = "$(Join-Path $nativeAssetRoot 'lib')$([IO.Path]::PathSeparator)$nativeAssetRoot"
  $env:GEM_HOME = $gemHome
  $env:GEM_PATH = $gemHome
  $env:GHL_DEPENDENCY_MANIFEST = $manifestPath
  Invoke-Checked (Join-Path $nativeAssetRoot 'bin/ruby.exe') (Join-Path $scriptRoot 'validate.rb')
}
finally {
  $env:RUBYLIB = $previousRubyLib
  $env:GEM_HOME = $previousGemHome
  $env:GEM_PATH = $previousGemPath
  $env:GHL_DEPENDENCY_MANIFEST = $previousManifest
}

Get-ChildItem -LiteralPath $nativeAssetRoot -Recurse -Force -File |
  Where-Object { $_.Name.StartsWith('.', [StringComparison]::Ordinal) } |
  Remove-Item -Force

Write-Provenance $manifest $nativeAssetRoot $pacmanPackages $rubyDescription
Write-Host "Staged complete Windows native closure: $nativeAssetRoot"
