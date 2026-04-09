# Copies a VHDX file with progress reporting. Internal helper for New-LabVM.
Function New-LabVMDrive {
    param( [string]$From, [string]$To)
    $ffile = $null
    $tofile = $null
    Write-Progress `
        -Activity "Copying file" `
        -status ($From.Split("\") | Select-Object -last 1) `
        -PercentComplete 0
    try {
        $ffile = [io.file]::OpenRead($From)
        $tofile = [io.file]::OpenWrite($To)
        $sw = [System.Diagnostics.Stopwatch]::StartNew();
        [byte[]]$buff = new-object byte[] (4096 * 1024)
        [long]$total = [long]$count = 0
        do {
            $count = $ffile.Read($buff, 0, $buff.Length)
            $tofile.Write($buff, 0, $count)
            $total += $count
            [int]$pctcomp = ([int]($total / $ffile.Length * 100));
            [int]$secselapsed = [int]($sw.elapsedmilliseconds.ToString()) / 1000;
            if ( $secselapsed -ne 0 ) {
                [single]$xferrate = (($total / $secselapsed) / 1mb);
            }
            else {
                [single]$xferrate = 0.0
            }
            if ($total % 1mb -eq 0) {
                if ($pctcomp -gt 0)`
                {
                    [int]$secsleft = ((($secselapsed / $pctcomp) * 100) - $secselapsed);
                }
                else {
                    [int]$secsleft = 0
                };
                Write-Progress `
                    -Activity ($pctcomp.ToString() + "% Copying file @ " + "{0:n2}" -f $xferrate + " MB/s")`
                    -status ($From.Split("\") | Select-Object -last 1) `
                    -PercentComplete $pctcomp `
                    -SecondsRemaining $secsleft;
            }
        } while ($count -gt 0)
        $sw.Stop();
        $sw.Reset();
    }
    finally {
        Write-Progress -Activity "Copying file" -Status "Ready" -Completed
        if ($null -ne $ffile -and $secselapsed -gt 0) {
            Write-Log (($from.Split("\") | Select-Object -last 1) + `
                    " copied in " + $secselapsed + " seconds at " + `
                    ("{0:n2}" -f [int](($ffile.length / $secselapsed) / 1mb)) + " MB/s.")
        }
        elseif ($null -ne $ffile) {
            Write-Log (($from.Split("\") | Select-Object -last 1) + " copied in under 1 second.")
        }
        if ($null -ne $ffile)  { $ffile.Close() }
        if ($null -ne $tofile) { $tofile.Close() }
    }
}