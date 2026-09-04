[CmdletBinding()]
param(
  [string] $NativeAssetRoot,
  [string] $HeaderPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../..')).Path
if (-not $NativeAssetRoot) {
  $NativeAssetRoot = Join-Path $repoRoot '.tmp/artifacts/native/win-x64'
}
if (-not $HeaderPath) {
  $HeaderPath = Join-Path $repoRoot 'src/MBW.GHLinguist.Native/include/ghlinguist.h'
}

$NativeAssetRoot = (Resolve-Path -LiteralPath $NativeAssetRoot).Path
$HeaderPath = (Resolve-Path -LiteralPath $HeaderPath).Path
$nativeLibrary = Join-Path $NativeAssetRoot 'ghlinguist.dll'
if (-not (Test-Path -LiteralPath $nativeLibrary -PathType Leaf)) {
  throw "The Windows native bridge is missing: $nativeLibrary"
}

function Find-Dumpbin {
  $command = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
  if ($command) {
    return $command.Source
  }

  $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
  if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'dumpbin.exe is unavailable and Visual Studio Installer could not be located.'
  }

  $installation = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
  if ($LASTEXITCODE -ne 0 -or -not $installation) {
    throw 'Unable to locate Visual Studio C++ tools for dumpbin.exe.'
  }

  $versionPath = Join-Path $installation 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt'
  if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
    throw "Visual Studio C++ tool version metadata is missing: $versionPath"
  }
  $version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
  $dumpbin = Join-Path $installation "VC/Tools/MSVC/$version/bin/Hostx64/x64/dumpbin.exe"
  if (-not (Test-Path -LiteralPath $dumpbin -PathType Leaf)) {
    throw "dumpbin.exe is missing from the Visual Studio C++ tools: $dumpbin"
  }
  return $dumpbin
}

function Invoke-Dumpbin {
  param([Parameter(Mandatory)] [string[]] $Arguments)

  $output = & $dumpbin @Arguments 2>&1 | Out-String
  if ($LASTEXITCODE -ne 0) {
    throw "dumpbin.exe failed: $output"
  }
  return $output
}

$header = Get-Content -LiteralPath $HeaderPath -Raw
$expectedExports = @([regex]::Matches($header, '\bGHL_CALL\s+(ghl_[A-Za-z0-9_]+)\s*\(') |
  ForEach-Object { $_.Groups[1].Value } |
  Sort-Object -Unique)
if ($expectedExports.Count -eq 0) {
  throw 'The public header does not declare any exported ghl_* functions.'
}

$dumpbin = Find-Dumpbin
$headers = Invoke-Dumpbin @('/nologo', '/headers', $nativeLibrary)
if ($headers -notmatch '(?im)^\s*8664 machine \(x64\)') {
  throw 'ghlinguist.dll is not an x64 PE image.'
}

$exportOutput = Invoke-Dumpbin @('/nologo', '/exports', $nativeLibrary)
$actualExports = @($exportOutput -split "`r?`n" |
  ForEach-Object { if ($_ -match '\b(ghl_[A-Za-z0-9_]+)\s*$') { $Matches[1] } } |
  Sort-Object -Unique)
if (($actualExports -join '|') -cne ($expectedExports -join '|')) {
  throw "Windows bridge exports do not match the public C ABI. Expected: $($expectedExports -join ', '). Actual: $($actualExports -join ', ')."
}

$dependencyOutput = Invoke-Dumpbin @('/nologo', '/dependents', $nativeLibrary)
$dependencies = @($dependencyOutput -split "`r?`n" |
  ForEach-Object { if ($_ -match '^\s+([A-Za-z0-9_.+-]+\.dll)\s*$') { $Matches[1] } } |
  Sort-Object -Unique)
if ('x64-ucrt-ruby400.dll' -notin $dependencies) {
  throw 'ghlinguist.dll does not depend on the pinned CRuby runtime DLL.'
}

$systemDirectory = [Environment]::SystemDirectory
foreach ($dependency in $dependencies) {
  if ($dependency -match '^(?i:api-ms-win-|ext-ms-win-)' -or
      (Test-Path -LiteralPath (Join-Path $systemDirectory $dependency) -PathType Leaf)) {
    continue
  }
  if ((Test-Path -LiteralPath (Join-Path $NativeAssetRoot $dependency) -PathType Leaf) -or
      (Test-Path -LiteralPath (Join-Path $NativeAssetRoot "bin/$dependency") -PathType Leaf)) {
    continue
  }
  throw "Windows bridge dependency is neither OS-provided nor present in the native closure: $dependency"
}

Write-Host "Validated $($actualExports.Count) Windows C ABI exports and $($dependencies.Count) native dependencies."
