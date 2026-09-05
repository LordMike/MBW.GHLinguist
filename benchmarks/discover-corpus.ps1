[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $CorpusRoot,
    [Parameter(Mandatory)] [string] $OutputRoot,
    [ValidateRange(1, 500)] [int] $MaximumArchives = 100,
    [ValidateRange(1, 1000000)] [int] $MaximumFilesToInspect = 10000,
    [ValidateRange(1, 10000)] [int] $MaximumEntriesPerArchive = 500,
    [ValidateRange(1024, 1073741824)] [long] $MaximumUncompressedBytes = 33554432,
    [ValidateRange(1, 1000)] [int] $MaximumFixtures = 64
)

$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $CorpusRoot -PathType Container)) { throw "Corpus root does not exist: $CorpusRoot" }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$fixtureRoot = Join-Path $OutputRoot 'fixtures'
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function IsSafeEntryName([string] $name) {
    return $name -and !$name.StartsWith('/') -and !$name.StartsWith('\\') -and $name -notmatch '^[A-Za-z]:' -and $name -notmatch '(^|[\\/])\.\.([\\/]|$)'
}
function IsText([byte[]] $bytes) {
    if ($bytes.Length -eq 0 -or $bytes -contains 0) { return $false }
    $control = @($bytes | Where-Object { ($_ -lt 9) -or ($_ -gt 13 -and $_ -lt 32) }).Count
    return $control * 100 -lt $bytes.Length
}

$archives = Get-ChildItem -LiteralPath $CorpusRoot -File -Recurse | Select-Object -First $MaximumFilesToInspect | Where-Object {
    $stream = [IO.File]::OpenRead($_.FullName)
    try { $signature = New-Object byte[] 4; $stream.Read($signature, 0, 4) | Out-Null; $signature[0] -eq 80 -and $signature[1] -eq 75 -and $signature[2] -eq 3 -and $signature[3] -eq 4 } finally { $stream.Dispose() }
} | Sort-Object FullName | Select-Object -First $MaximumArchives
$candidates = [Collections.Generic.List[object]]::new()
foreach ($archiveFile in $archives) {
    try { $archive = [IO.Compression.ZipFile]::OpenRead($archiveFile.FullName) } catch { continue }
    try {
        if ($archive.Entries.Count -gt $MaximumEntriesPerArchive) { continue }
        $total = 0L
        foreach ($entry in $archive.Entries) {
            if (!(IsSafeEntryName $entry.FullName) -or $entry.Length -gt $MaximumUncompressedBytes) { continue }
            $total += $entry.Length
            if ($total -gt $MaximumUncompressedBytes) { break }
            if ($entry.Length -lt 16 -or $entry.Length -gt 524288) { continue }
            $stream = $entry.Open(); try { $memory = [IO.MemoryStream]::new(); $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
            if (!(IsText $bytes)) { continue }
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            $extension = [IO.Path]::GetExtension($entry.Name).ToLowerInvariant(); if (!$extension) { $extension = '.none' }
            $candidates.Add([pscustomobject]@{ Archive = $archiveFile.Name; Entry = $entry.FullName; Extension = $extension; Hash = $hash; Bytes = $bytes })
        }
    } finally { $archive.Dispose() }
}
$selected = $candidates | Sort-Object Extension, Hash | Group-Object Extension | ForEach-Object { $_.Group | Select-Object -First 1 } | Sort-Object Hash | Select-Object -First $MaximumFixtures
$manifest = foreach ($item in $selected) {
    $name = "$($item.Hash.Substring(0,16))$($item.Extension)"
    [IO.File]::WriteAllBytes((Join-Path $fixtureRoot $name), $item.Bytes)
    [pscustomobject]@{ File = $name; Extension = $item.Extension; Sha256 = $item.Hash; Bytes = $item.Bytes.Length }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot 'manifest.json') -Encoding utf8
Write-Host "Selected $($selected.Count) text fixtures from $($archives.Count) archives."
