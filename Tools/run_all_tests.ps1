# Rebuilds the project content, then runs EditMode and PlayMode suites headless. Results land in Documentation/.
# Usage: powershell -File Tools\run_all_tests.ps1   (set $env:UNITY_EXE to override the editor path)
$ErrorActionPreference = "Continue"
$unity = if ($env:UNITY_EXE) { $env:UNITY_EXE } else { "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" }
$proj  = Split-Path -Parent $PSScriptRoot
$docs  = Join-Path $proj "Documentation"
$common = @('-batchmode','-nographics','-burst-disable-compilation','-projectPath',$proj)

function Run-Unity($extra, $log) {
    $p = Start-Process $unity -ArgumentList ($common + $extra + @('-logFile', (Join-Path $proj $log))) -PassThru -Wait
    return $p.ExitCode
}
function Summarise($xmlPath) {
    if (-not (Test-Path $xmlPath)) { return "no result file" }
    $x = [xml](Get-Content $xmlPath); $r = $x.'test-run'
    $line = "total=$($r.total) passed=$($r.passed) failed=$($r.failed) skipped=$($r.skipped) result=$($r.result)"
    $x.SelectNodes("//test-case[@result='Failed']") | ForEach-Object { $line += "`n  FAIL $($_.fullname): " + ($_.failure.message.'#cdata-section' -replace "`n", " ") }
    return $line
}

"== build =="
$b = Run-Unity @('-quit','-executeMethod','MedSimXR.EditorTools.ProjectBuilder.BuildAllAndExit') "Logs_build.log"
"build exit=$b"
python -I (Join-Path $proj "Tools\AssetValidator\build_manifest.py")

"== EditMode =="
$e = Run-Unity @('-runTests','-testPlatform','EditMode','-testResults',(Join-Path $docs 'TestResults_EditMode.xml')) "Logs_edittests.log"
"exit=$e  " + (Summarise (Join-Path $docs 'TestResults_EditMode.xml'))

"== PlayMode =="
$pl = Run-Unity @('-runTests','-testPlatform','PlayMode','-testResults',(Join-Path $docs 'TestResults_PlayMode.xml')) "Logs_playtests.log"
"exit=$pl  " + (Summarise (Join-Path $docs 'TestResults_PlayMode.xml'))
"== DONE =="
