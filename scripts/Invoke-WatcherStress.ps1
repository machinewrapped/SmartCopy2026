param(
    [int]$Iterations = 1000
)

$pingPath = Join-Path (Get-Location) "ping.tmp"
$pongPath = Join-Path (Get-Location) "pong.tmp"

New-Item -Path $pingPath -ItemType File -Force | Out-Null
Write-Host "Renaming file between ping.tmp and pong.tmp $Iterations times..."

for ($i = 0; $i -lt $Iterations; $i++) {
    Rename-Item -Path $pingPath -NewName "pong.tmp" -Force
    Rename-Item -Path $pongPath -NewName "ping.tmp" -Force
}

Remove-Item -Path $pingPath -Force