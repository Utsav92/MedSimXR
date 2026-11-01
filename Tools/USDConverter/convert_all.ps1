$b="C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"; $raw="D:\MedSimXR\Tools\AssetDownloader\raw"
$log="D:\MedSimXR\Tools\USDConverter\batch.log"; "" | Set-Content $log
$jobs=@(@('rack_large_empty_a02',15000),@('box_corrugated_brown_b20',6000),@('CT_injection_system_a01',40000))
foreach($j in $jobs){ $id=$j[0]; $usd=(Get-ChildItem "$raw\$id\simready_usd" -Filter "sm_*.usd" | Select -First 1).FullName
 "[$id] start $(Get-Date -f T)" | Add-Content $log
 & $b --background --python D:\MedSimXR\Tools\USDConverter\convert_usd.py -- $usd "D:\MedSimXR\Assets\SimReady\$id.glb" "D:\MedSimXR\Tools\USDConverter\reports\$id.json" $j[1] 1024 2>&1 | Select-String 'INFO tris|EXPORTED|Error|Traceback|WARNING' | Add-Content $log
 "[$id] exit=$LASTEXITCODE $(Get-Date -f T)" | Add-Content $log }
"ALL DONE" | Add-Content $log
