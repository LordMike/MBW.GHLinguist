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

function Write-Provenance {
  param([Parameter(Mandatory)] $Manifest, [Parameter(Mandatory)] [string] $Root)

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
    schemaVersion = 1
    platform = 'win-x64'
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    rubyVersion = $Manifest.ruby.version
    linguistVersion = $Manifest.linguist.version
    linguistRevision = $Manifest.linguist.revision
    files = @($files)
  } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Root 'provenance.json') -Encoding utf8NoBOM
}

foreach ($command in 'git', 'cmake') {
  if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
    throw "Required command is unavailable: $command"
  }
}

Require-Path $manifestPath 'native dependency manifest'
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

Remove-Item -LiteralPath $buildRoot, $nativeAssetRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $buildRoot, $nativeAssetRoot -Force | Out-Null

# Keep Ruby's executable and all adjacent DLLs together so its loader never falls back to a machine-wide Ruby.
New-Item -ItemType Directory -Path (Join-Path $nativeAssetRoot 'bin') -Force | Out-Null
Copy-Item -LiteralPath $ruby -Destination (Join-Path $nativeAssetRoot 'bin/ruby.exe')
Get-ChildItem -LiteralPath (Join-Path $RubyRoot 'bin') -Filter '*.dll' -File |
  Copy-Item -Destination (Join-Path $nativeAssetRoot 'bin') -Force
Copy-RequiredDirectory (Join-Path $RubyRoot 'lib/ruby') (Join-Path $nativeAssetRoot 'lib/ruby') 'Ruby standard library'

$gemHome = Join-Path $nativeAssetRoot "lib/ruby/gems/$($manifest.ruby.abiVersion)"
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
}

foreach ($pattern in $manifest.icu.windowsPatterns) {
  $matches = foreach ($searchPath in $manifest.icu.windowsSearchPaths) {
    Get-ChildItem -Path (Join-Path (Join-Path $RubyRoot $searchPath) $pattern) -File
  }
  if (-not $matches) {
    throw "Required ICU dependency matching '$pattern' is missing under $RubyRoot/bin or $RubyRoot/msys64/ucrt64/bin."
  }
  $matches | Copy-Item -Destination (Join-Path $nativeAssetRoot 'bin') -Force
}

foreach ($path in $manifest.linguist.paths) {
  if ($path -eq 'lib') {
    Copy-RequiredDirectoryContents (Join-Path $LinguistRoot $path) (Join-Path $nativeAssetRoot 'lib') "Linguist $path"
  }
  else {
    Copy-RequiredDirectory (Join-Path $LinguistRoot $path) (Join-Path $nativeAssetRoot "linguist/$path") "Linguist $path"
  }
}
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

$bridgeBuild = Join-Path $buildRoot 'bridge'
Invoke-Checked cmake '-S' (Join-Path $repoRoot 'src/MBW.GHLinguist.Native') '-B' $bridgeBuild '-G' 'MinGW Makefiles' "-DGHL_RUBY_ROOT=$RubyRoot"
Invoke-Checked cmake '--build' $bridgeBuild '--parallel' '2'
$bridge = Get-ChildItem -LiteralPath $bridgeBuild -Filter 'ghlinguist.dll' -File -Recurse | Select-Object -First 1
if (-not $bridge) {
  throw 'ghlinguist bridge build completed without producing ghlinguist.dll.'
}
Copy-Item -LiteralPath $bridge.FullName -Destination (Join-Path $nativeAssetRoot 'ghlinguist.dll') -Force

$previousRubyLib, $previousGemHome, $previousGemPath, $previousManifest = $env:RUBYLIB, $env:GEM_HOME, $env:GEM_PATH, $env:GHL_DEPENDENCY_MANIFEST
try {
  $env:RUBYLIB = Join-Path $nativeAssetRoot 'lib'
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

Write-Provenance $manifest $nativeAssetRoot
Write-Host "Staged complete Windows native closure: $nativeAssetRoot"
