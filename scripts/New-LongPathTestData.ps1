param(
    [string]$Root = (Join-Path (Get-Location) "LongPathTest")
)

# Build a deep path that exceeds Windows' classic MAX_PATH (260 chars).
# Each segment is 10 chars; keep nesting until the path to a file inside exceeds 260.
$seg = "aaaaaaaaaa"
$cur = $Root
while ([System.IO.Path]::Combine($cur, "file.txt").Length -le 260) {
    $cur = [System.IO.Path]::Combine($cur, $seg)
}

New-Item -ItemType Directory -Force -Path $cur | Out-Null

# Seed files at the deep end
"long-path content"  | Set-Content -Path (Join-Path $cur "deep.txt")
"another deep file"  | Set-Content -Path (Join-Path $cur "deep2.txt")

# Shallow file at the root for contrast
"shallow content" | Set-Content -Path (Join-Path $Root "shallow.txt")

$deepFile = Join-Path $cur "deep.txt"
Write-Host "Root:      $Root"
Write-Host "Deep dir:  $cur"
Write-Host "Deep file: $deepFile"
Write-Host "Path length: $($deepFile.Length) chars (must be > 260)"
Write-Host ""
Write-Host "Done. Point SmartCopy at '$Root' to test browse / copy / move / delete."
Write-Host "To clean up: Remove-Item -Recurse -Force '$Root'"
