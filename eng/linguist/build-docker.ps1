[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../..')).Path
$manifestPath = Join-Path $scriptRoot 'native-dependencies.json'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw 'Required command is unavailable: docker'
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
  throw 'Required command is unavailable: git'
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
  throw "Native dependency manifest is missing: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$linguistRoot = Join-Path $repoRoot 'extern/linguist'
if (-not (Test-Path -LiteralPath (Join-Path $linguistRoot 'ext/linguist/extconf.rb'))) {
  throw 'Linguist is not checked out. Run: git submodule update --init extern/linguist'
}

$actualRevision = (& git -C $linguistRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
  throw 'Unable to read the Linguist revision.'
}
if ($actualRevision -ne $manifest.linguist.revision) {
  throw "Expected Linguist revision $($manifest.linguist.revision), found $actualRevision."
}

$imageTag = 'ghlinguist-build:linux-x64'
& docker build --build-arg "RUBY_IMAGE=$($manifest.ruby.dockerImage)" --tag $imageTag $scriptRoot
if ($LASTEXITCODE -ne 0) {
  throw 'Failed to build the Linguist build image.'
}

$dockerArguments = @(
  'run'
  '--rm'
  '--mount', "type=bind,source=$repoRoot,target=/workspace"
)

if ($IsLinux -or $IsMacOS) {
  $uid = (& id -u).Trim()
  $gid = (& id -g).Trim()
  $dockerArguments += @('--user', "${uid}:${gid}")
}

$dockerArguments += $imageTag
& docker @dockerArguments
if ($LASTEXITCODE -ne 0) {
  throw 'The Linux Linguist build failed.'
}
