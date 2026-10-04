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

# Behind an HTTPS proxy (HTTPS_PROXY) with its own trust store (SSL_CERT_FILE), the
# containers use the host network to reach the proxy and trust the same CA bundle.
$httpsProxy = if ($env:HTTPS_PROXY) { $env:HTTPS_PROXY } else { $env:https_proxy }
$noProxy = if ($env:NO_PROXY) { $env:NO_PROXY } else { $env:no_proxy }
$caBundle = if ($env:SSL_CERT_FILE -and (Test-Path -LiteralPath $env:SSL_CERT_FILE -PathType Leaf)) { (Resolve-Path -LiteralPath $env:SSL_CERT_FILE).Path } else { $null }

$imageTag = 'ghlinguist-build:linux-x64'
$buildArguments = @(
  'build'
  '--build-arg', "RUBY_IMAGE=$($manifest.ruby.dockerImage)"
  '--tag', $imageTag
)
if ($httpsProxy) {
  $buildArguments += @('--network', 'host', '--build-arg', "https_proxy=$httpsProxy")
  if ($noProxy) {
    $buildArguments += @('--build-arg', "no_proxy=$noProxy")
  }
}
if ($caBundle) {
  $buildArguments += @('--secret', "id=ca-bundle,src=$caBundle")
}
$buildArguments += $scriptRoot
& docker @buildArguments
if ($LASTEXITCODE -ne 0) {
  throw 'Failed to build the Linguist build image.'
}

$dockerArguments = @(
  'run'
  '--rm'
  '--env', "LINGUIST_REVISION=$actualRevision"
  '--mount', "type=bind,source=$repoRoot,target=/workspace"
)

if ($httpsProxy) {
  $dockerArguments += @('--network', 'host', '--env', "https_proxy=$httpsProxy")
  if ($noProxy) {
    $dockerArguments += @('--env', "no_proxy=$noProxy")
  }
}
if ($caBundle) {
  $dockerArguments += @(
    '--mount', "type=bind,source=$caBundle,target=/run/ghlinguist/ca-bundle.crt,readonly"
    '--env', 'SSL_CERT_FILE=/run/ghlinguist/ca-bundle.crt'
  )
}

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
