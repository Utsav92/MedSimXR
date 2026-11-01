# Usage: powershell -File download_list.ps1 <listfile>   (one asset id per line)
# hf download needs one --include pattern per call, so each asset is its own call.
# source_assets/ only duplicates Textures/ (about half the bytes), so it is excluded. hf skips files it already has.
param([string]$List)
$raw = "D:\MedSimXR\Tools\AssetDownloader\raw"
$log = [IO.Path]::ChangeExtension($List, ".log")
"" | Set-Content $log
foreach ($id in (Get-Content $List | Where-Object { $_.Trim() })) {
    if (Test-Path "$raw\$id\.done") { "[$id] done earlier" | Add-Content $log; continue }
    "[$id] start $(Get-Date -f T)" | Add-Content $log
    & hf download nvidia/simready-assets --repo-type dataset --include "$id/*" --exclude "*/source_assets/*" --local-dir $raw 2>&1 | Select-Object -Last 1 | Add-Content $log
    $code = $LASTEXITCODE
    if ($code -eq 0) { "ok" | Set-Content "$raw\$id\.done" }
    "[$id] exit=$code $(Get-Date -f T)" | Add-Content $log
}
"ALL DONE" | Add-Content $log
