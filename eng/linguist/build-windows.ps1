[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../..')).Path
$linguistRoot = Join-Path $repoRoot 'extern/linguist'
$buildRoot = Join-Path $repoRoot '.tmp/build/linguist/win-x64'
$artifactRoot = Join-Path $repoRoot '.tmp/artifacts/linguist/win-x64'
$extensionSource = Join-Path $buildRoot 'extension'
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

foreach ($command in 'git', 'ruby') {
  if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
    throw "Required command is unavailable: $command"
  }
}

if (-not (Get-Command make -ErrorAction SilentlyContinue)) {
  $rubyRoot = (& ruby -rrbconfig -e 'print RbConfig::CONFIG["prefix"]')
  if ($LASTEXITCODE -ne 0) {
    throw 'Unable to locate the Ruby installation.'
  }

  $msysPaths = @(
    (Join-Path $rubyRoot 'msys64/usr/bin')
    (Join-Path $rubyRoot 'msys64/ucrt64/bin')
  )
  if ($msysPaths | Where-Object { -not (Test-Path -LiteralPath $_) }) {
    throw 'Required command is unavailable: make. Install Ruby with its MSYS2 Devkit.'
  }

  $env:Path = ($msysPaths -join [IO.Path]::PathSeparator) + [IO.Path]::PathSeparator + $env:Path
}

foreach ($command in 'make', 'gcc') {
  if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
    throw "Required command is unavailable: $command"
  }
}

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

$actualLinguistVersion = (Get-Content -LiteralPath (Join-Path $linguistRoot 'lib/linguist/VERSION') -Raw).Trim()
if ($actualLinguistVersion -ne $versions.LINGUIST_VERSION) {
  throw "Expected Linguist $($versions.LINGUIST_VERSION), found $actualLinguistVersion."
}

$actualRubyVersion = (& ruby -e 'print RUBY_VERSION').Trim()
if ($LASTEXITCODE -ne 0) {
  throw 'Unable to read the Ruby version.'
}
if ($actualRubyVersion -ne $versions.RUBY_VERSION) {
  throw "Expected Ruby $($versions.RUBY_VERSION), found $actualRubyVersion."
}

Remove-Item -LiteralPath $buildRoot, $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $extensionSource -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $artifactRoot 'lib') -Force | Out-Null
Copy-Item -Path (Join-Path $linguistRoot 'ext/linguist/*') -Destination $extensionSource -Recurse
Copy-Item -Path (Join-Path $linguistRoot 'lib/*') -Destination (Join-Path $artifactRoot 'lib') -Recurse

Push-Location $extensionSource
try {
  Invoke-Checked ruby extconf.rb
  Invoke-Checked make '-j2'
}
finally {
  Pop-Location
}

$extension = Get-ChildItem -LiteralPath $extensionSource -File |
  Where-Object { $_.Name -in @('linguist.so', 'linguist.dll') } |
  Select-Object -First 1
if (-not $extension) {
  throw 'The Linguist extension was not produced.'
}

$extensionDestination = Join-Path $artifactRoot "lib/linguist/$($extension.Name)"
Copy-Item -LiteralPath $extension.FullName -Destination $extensionDestination

$previousRubyLib = $env:RUBYLIB
try {
  $env:RUBYLIB = Join-Path $artifactRoot 'lib'
  Invoke-Checked ruby (Join-Path $scriptRoot 'validate.rb')
}
finally {
  $env:RUBYLIB = $previousRubyLib
}

Write-Host "Linguist Windows artifacts: $artifactRoot"
