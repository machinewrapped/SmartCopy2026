param(
    [int]$Iterations = 1000
)

$f = New-Item -Path (Join-Path (Get-Location) "ping.tmp") -ItemType File -Force
Write-Host "Renaming $($f.FullName) $Iterations times..."

for ($i = 0; $i -lt $Iterations; $i++) {
    Rename-Item $f.FullName "pong.tmp" -Force
    $f = Get-Item (Join-Path (Get-Location) "pong.tmp")
    Rename-Item $f.FullName "ping.tmp" -Force
    $f = Get-Item (Join-Path (Get-Location) "ping.tmp")
}

Remove-Item $f.FullName -Force
Write-Host "Done."
