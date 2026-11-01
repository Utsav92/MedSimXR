# Usage: powershell -File convert_list.ps1 <download list file>
# Waits for each asset's download marker, then converts USD -> GLB with headless Blender (decimate + texture cap).
param([string]$List, [string]$After = "")
$b = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
$raw = "D:\MedSimXR\Tools\AssetDownloader\raw"
$jobs = @{}
Import-Csv -Header id,tris,tex D:\MedSimXR\Tools\USDConverter\jobs.csv | ForEach-Object { $jobs[$_.id] = $_ }
$log = "D:\MedSimXR\Tools\USDConverter\" + [IO.Path]::GetFileNameWithoutExtension($List) + "_convert.log"
"" | Set-Content $log
if ($After) { while (-not ((Test-Path $After) -and (Select-String -Path $After -Pattern "ALL DONE" -Quiet))) { Start-Sleep 20 } }
foreach ($id in (Get-Content $List | Where-Object { $_.Trim() })) {
    $j = $jobs[$id]
    if (-not $j) { "[$id] no job entry" | Add-Content $log; continue }
    $out = "D:\MedSimXR\Assets\SimReady\$id.glb"
    if (Test-Path $out) { "[$id] glb exists" | Add-Content $log; continue }
    $waited = 0
    while (-not (Test-Path "$raw\$id\.done") -and $waited -lt 7200) { Start-Sleep 15; $waited += 15 }
    if (-not (Test-Path "$raw\$id\.done")) { "[$id] download never finished" | Add-Content $log; continue }
    $usd = (Get-ChildItem "$raw\$id\simready_usd" -Filter "sm_*.usd" | Select-Object -First 1).FullName
    "[$id] start $(Get-Date -f T) $usd" | Add-Content $log
    & $b --background --python D:\MedSimXR\Tools\USDConverter\convert_usd.py -- $usd $out "D:\MedSimXR\Tools\USDConverter\reports\$id.json" $j.tris $j.tex 2>&1 | Select-String 'INFO tris|EXPORTED|Error|Traceback|WARNING' | Add-Content $log
    "[$id] exit=$LASTEXITCODE glb=$(Test-Path $out) $(Get-Date -f T)" | Add-Content $log
}
"ALL DONE" | Add-Content $log
