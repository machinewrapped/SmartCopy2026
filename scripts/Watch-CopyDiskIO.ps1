<#
.SYNOPSIS
    Samples disk, cache and process I/O counters during a copy so SmartCopy and Explorer can be compared.

.EXAMPLE
    ./scripts/Watch-CopyDiskIO.ps1 -Source F -Destination M -Label explorer
    ./scripts/Watch-CopyDiskIO.ps1 -Source F -Destination M -Label smartcopy

    Start the script, start the copy, Ctrl+C when done (or use -Seconds). A CSV is written per run.
#>
param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Destination,
    [string] $Label = 'run',
    [int] $Seconds = 0,
    [string] $OutDir = (Join-Path $PSScriptRoot 'diskio-logs')
)

$ErrorActionPreference = 'Stop'

function Get-DiskInstance([string] $letter) {
    $letter = $letter.TrimEnd(':', '\').ToUpperInvariant()
    $instance = (Get-Counter -ListSet PhysicalDisk).PathsWithInstances |
        ForEach-Object { if ($_ -match '^\\PhysicalDisk\((.+?)\)\\') { $Matches[1] } } |
        Sort-Object -Unique |
        Where-Object { $_ -match "(^|\s)${letter}:" } |
        Select-Object -First 1
    if (-not $instance) { throw "No PhysicalDisk counter instance found for drive ${letter}:" }
    $instance
}

$src = Get-DiskInstance $Source
$dst = Get-DiskInstance $Destination
$sameDisk = $src -eq $dst

# Column name -> counter path. Process counters use wildcards; missing processes read as 0.
$columns = [ordered]@{
    'SrcReadMBps'       = "\PhysicalDisk($src)\Disk Read Bytes/sec"
    'SrcReadKiB/IO'     = "\PhysicalDisk($src)\Avg. Disk Bytes/Read"
    'SrcReadQueue'      = "\PhysicalDisk($src)\Avg. Disk Read Queue Length"
    'SrcReadMs'         = "\PhysicalDisk($src)\Avg. Disk sec/Read"
    'SrcIdlePct'        = "\PhysicalDisk($src)\% Idle Time"
    'DstWriteMBps'      = "\PhysicalDisk($dst)\Disk Write Bytes/sec"
    'DstWriteKiB/IO'    = "\PhysicalDisk($dst)\Avg. Disk Bytes/Write"
    'DstWriteQueue'     = "\PhysicalDisk($dst)\Avg. Disk Write Queue Length"
    'DstIdlePct'        = "\PhysicalDisk($dst)\% Idle Time"
    'DirtyMB'           = '\Cache\Dirty Pages'
    'ModifiedMB'        = '\Memory\Modified Page List Bytes'
    'SC_ReadMBps'       = '\Process(SmartCopy*)\IO Read Bytes/sec'
    'SC_WriteMBps'      = '\Process(SmartCopy*)\IO Write Bytes/sec'
    'SC_WorkingSetMB'   = '\Process(SmartCopy*)\Working Set - Private'
    'Exp_ReadMBps'      = '\Process(explorer*)\IO Read Bytes/sec'
    'Exp_WriteMBps'     = '\Process(explorer*)\IO Write Bytes/sec'
}

$paths = @($columns.Values)
$scale = @{
    'SrcReadMBps' = 1MB; 'DstWriteMBps' = 1MB; 'SrcReadKiB/IO' = 1KB; 'DstWriteKiB/IO' = 1KB
    'SrcReadMs' = 0.001; 'DirtyMB' = (1MB / 4KB); 'ModifiedMB' = 1MB
    'SC_ReadMBps' = 1MB; 'SC_WriteMBps' = 1MB; 'SC_WorkingSetMB' = 1MB
    'Exp_ReadMBps' = 1MB; 'Exp_WriteMBps' = 1MB
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$csv = Join-Path $OutDir ("{0}-{1:yyyyMMdd-HHmmss}.csv" -f $Label, (Get-Date))

Write-Host "Source: $src   Destination: $dst$(if ($sameDisk) { '   (SAME DISK)' })"
Write-Host "Logging to $csv  -- Ctrl+C to stop"
Write-Host ('{0,8} {1,8} {2,6} {3,6} {4,5} | {5,8} {6,8} {7,6} {8,5} | {9,7} {10,7}' -f
    'rd MB/s', 'rd KiB', 'rdQ', 'rd ms', 'idle', 'wr MB/s', 'wr KiB', 'wrQ', 'idle', 'dirtyMB', 'modMB')

$rows = [System.Collections.Generic.List[object]]::new()
$start = Get-Date
try {
    while ($Seconds -le 0 -or ((Get-Date) - $start).TotalSeconds -lt $Seconds) {
        $sample = Get-Counter -Counter $paths -SampleInterval 1 -MaxSamples 1 -ErrorAction SilentlyContinue
        $row = [ordered]@{ Time = (Get-Date).ToString('HH:mm:ss') }
        foreach ($name in $columns.Keys) {
            $pattern = [regex]::Escape($columns[$name]).Replace('\*', '[^)]*')
            $value = ($sample.CounterSamples |
                Where-Object { $_.Path -match "$pattern$" } |
                Measure-Object -Property CookedValue -Sum).Sum
            if ($null -eq $value) { $value = 0 }
            if ($scale.ContainsKey($name)) { $value = $value / $scale[$name] }
            $row[$name] = [math]::Round($value, 2)
        }
        $obj = [pscustomobject]$row
        $rows.Add($obj)
        $obj | Export-Csv -Path $csv -Append -NoTypeInformation

        Write-Host ('{0,8:N1} {1,8:N0} {2,6:N2} {3,6:N1} {4,5:N0} | {5,8:N1} {6,8:N0} {7,6:N2} {8,5:N0} | {9,7:N0} {10,7:N0}' -f
            $obj.SrcReadMBps, $obj.'SrcReadKiB/IO', $obj.SrcReadQueue, $obj.SrcReadMs, $obj.SrcIdlePct,
            $obj.DstWriteMBps, $obj.'DstWriteKiB/IO', $obj.DstWriteQueue, $obj.DstIdlePct,
            $obj.DirtyMB, $obj.ModifiedMB)
    }
}
finally {
    $active = $rows | Where-Object { $_.SrcReadMBps -gt 1 -or $_.DstWriteMBps -gt 1 }
    if ($active) {
        Write-Host "`nSummary over $($active.Count) active seconds ($Label):"
        foreach ($name in 'SrcReadMBps', 'SrcReadKiB/IO', 'SrcReadQueue', 'SrcReadMs', 'SrcIdlePct',
                          'DstWriteMBps', 'DstWriteKiB/IO', 'DstWriteQueue', 'DstIdlePct', 'DirtyMB') {
            $m = $active | Measure-Object -Property $name -Average -Maximum
            Write-Host ('  {0,-15} avg {1,10:N2}   max {2,10:N2}' -f $name, $m.Average, $m.Maximum)
        }
    }
}
