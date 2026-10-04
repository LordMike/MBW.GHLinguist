param([string]$Path = '.')
Get-ChildItem -Path $Path -Recurse -File |
    Where-Object { $_.Length -gt 1MB } |
    Select-Object FullName, Length
