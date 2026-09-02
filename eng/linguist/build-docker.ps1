[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../..')).Path
$versionsFile = Join-Path $scriptRoot 'versions.env'

function Read-Versions {
  $values = @{}
  foreach ($line in Get-Content -LiteralPath $versionsFile) {
    if ($line -match '^([^#=]+)=(.+)$') {
      $values[$Matches[1]] = $Matches[2]
    }
  }
  return $values
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw 'Required command is unavailable: docker'
}

$linguistRoot = Join-Path $repoRoot 'extern/linguist'
if (-not (Test-Path -LiteralPath (Join-Path $linguistRoot 'ext/linguist/extconf.rb'))) {
  throw 'Linguist is not checked out. Run: git submodule update --init extern/linguist'
}

$versions = Read-Versions
$actualRevision = (& git -C $linguistRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
  throw 'Unable to read the Linguist revision.'
}
if ($actualRevision -ne $versions.LINGUIST_REVISION) {
  throw "Expected Linguist revision $($versions.LINGUIST_REVISION), found $actualRevision."
}

$imageTag = 'originary-linguist-build:linux-x64'
& docker build --build-arg "RUBY_IMAGE=$($versions.RUBY_DOCKER_IMAGE)" --tag $imageTag $scriptRoot
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
