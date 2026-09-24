[CmdletBinding()]
param([string]$Apk, [string]$UnityEditor, [string]$OutDir)
# Device test of the Android Release APK (lightweight, automatable part of the Android acceptance).
# Installs the APK on the connected device (adb), starts it with the -remote harness and drives the
# REAL app with real touches (adb shell input tap/swipe), real rotation (system rotation setting,
# restored afterwards) and the real Home key. The harness only reads state and provides two test-only
# actions: opening the referee notice and a scripted player move. Screenshots via screencap.
# The final touch feel, layouts, sound and lifecycle are for a person on the device.
$ErrorActionPreference = 'Stop'
$editorVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ProjectSettings\ProjectVersion.txt') -Raw), '(?m)^m_EditorVersion:\s*(\S+)').Groups[1].Value
if (-not $UnityEditor) { $UnityEditor = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$editorVersion\Editor\Unity.exe" }
# Prefer the Android SDK's platform-tools (newer devices need a current adb); fall back to Unity's.
$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
if (-not (Test-Path -LiteralPath $adb)) { $adb = Join-Path (Split-Path $UnityEditor) 'Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' }
# Only one adb server version may run; restart it with the chosen adb (a Unity build starts its own).
Get-Process adb -ErrorAction SilentlyContinue | Where-Object { $_.Path -ne $adb } | Stop-Process -Force -ErrorAction SilentlyContinue
& $adb start-server | Out-Null
& $adb wait-for-device
# Several phones may be attached: use the first one that is ready (ANDROID_SERIAL pins every adb call).
if (-not $env:ANDROID_SERIAL) {
    $ready = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '^(\S+)\s+device$' } | ForEach-Object { ($_ -split '\s+')[0] })
    if ($ready.Count -eq 0) { throw 'No authorized device.' }
    $env:ANDROID_SERIAL = $ready[0]
}
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION.txt') -Raw).Trim()
if (-not $Apk) { $Apk = Join-Path $PSScriptRoot "bin\Android\Release-$version\AF-MilitaryShogi-$version.apk" }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot ('Builds\AndroidTest\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $OutDir | Out-Null
Import-Module (Join-Path $PSScriptRoot 'Tools\Remote\Remote.psm1') -Force
$pkg = 'com.af.militaryshogi'
$log = New-Object System.Collections.Generic.List[string]
$failures = 0
function Check([bool]$ok, [string]$what) { if (-not $ok) { $script:failures++ }; $line = ($(if ($ok) { 'OK   ' } else { 'FAIL ' }) + $what); $log.Add($line); Write-Host $line }
function Shot([string]$name) { & cmd /c "`"$adb`" exec-out screencap -p > `"$OutDir\$name`""; }
function State { return Send-Remote 'state' }
function WaitFor([scriptblock]$cond, [int]$sec = 30) {
    $t = [DateTime]::Now
    while (([DateTime]::Now - $t).TotalSeconds -lt $sec) { $s = State; if (& $cond $s) { return $s }; Start-Sleep -Milliseconds 400 }
    return State
}
function Spot($s, [string]$key) { if (-not $s.ContainsKey("spot.$key")) { throw "spot not recorded: $key" }; return $s["spot.$key"] }
function Snap($s) { return ($s.phase, $s.ply, $s.selected, $s.formation, $s.fingerprint, $s.strength, $s.temperament, $s.presetSlot, $s.presetSaving, $s.settingsOpen, $s.helpOpen) -join '|' }
function InSafe($s, [string]$point) {
    $p = Get-Point $point; $a = $s.safe -split ','
    return ($p[0] -ge [int]$a[0] -and $p[1] -ge [int]$a[1] -and $p[0] -le [int]$a[0] + [int]$a[2] -and $p[1] -le [int]$a[1] + [int]$a[3])
}

$model = (& $adb shell getprop ro.product.model).Trim()
$android = (& $adb shell getprop ro.build.version.release).Trim()
$log.Add("AF-MilitaryShogi $version Android device test on $model (Android $android)")
$rotAuto = (& $adb shell settings get system accelerometer_rotation).Trim()
$rotUser = (& $adb shell settings get system user_rotation).Trim()
try {
    # Stop the app before updating it: on Android 17 updating a running app and relaunching at once can
    # crash Unity's start-up (system "package updated" screen in the same task).
    & $adb shell am force-stop $pkg
    & $adb install -r $Apk | Out-Host
    Start-Sleep 3
    if ($LASTEXITCODE -ne 0) { throw 'adb install failed' }
    & $adb shell settings put system accelerometer_rotation 0
    & $adb shell settings put system user_rotation 0
    & $adb shell am force-stop $pkg
    # Fresh user data (settings/presets saved by earlier runs would change the checks, e.g. SE OFF).
    & $adb shell "rm -rf /sdcard/Android/data/$pkg/files/remote /sdcard/Android/data/$pkg/files/settings.txt /sdcard/Android/data/$pkg/files/presets.txt" | Out-Null
    $activity = (& $adb shell cmd package resolve-activity --brief $pkg | Select-Object -Last 1).Trim()
    & $adb shell am start -n $activity -e args '-remote' | Out-Null
    Connect-RemoteDevice $adb $pkg
    $deadline = [DateTime]::Now.AddSeconds(60)
    while (-not (Read-RemoteText 'state.txt')) { if ([DateTime]::Now -gt $deadline) { throw 'app did not start the remote harness' }; Start-Sleep 1 }
    Start-Sleep 2

    # --- Start-up: 対戦 only, sound, font, safe area ---
    $s = State
    $log.Add("screen $($s.screen) safe $($s.safe) dpi $($s.dpi) scale $($s.scale) font $($s.font)")
    Check ($s.mobile -eq 'True' -and $s.researchUi -eq 'False') 'Android layout, no research UI'
    Check ($s.listeners -eq '1' -and $s.clips -eq 'True') "exactly one AudioListener, all clips loaded"
    Check ($s.portrait -eq 'True') 'portrait at start'
    $spots = $s.Keys | Where-Object { $_ -like 'spot.mobile.*' }
    Check (@($spots | Where-Object { -not (InSafe $s $s[$_]) }).Count -eq 0) "portrait: all buttons inside the safe area ($($spots.Count) checked)"
    $s = Send-Remote 'seeds' '1001,2002,3003'
    Shot '01_portrait_setup.png'

    # --- Setup: tap own piece, tap another cell → swap ---
    $own = @($s.own -split ';' | ForEach-Object { $k, $v = $_ -split '@'; @{ Node = ($k -split ':')[0]; Type = ($k -split ':')[1]; At = $v } })
    $a = $own | Where-Object { $_.Type -eq 'General' } | Select-Object -First 1
    $b = $own | Where-Object { $_.Type -eq 'SecondLieutenant' } | Select-Object -First 1
    $f0 = $s.formation
    Invoke-Tap $a.At; $s = State
    Check ($s.selected -eq $a.Node) 'setup: tap selects own piece'
    Invoke-Tap $b.At; $s = State
    Check ($s.formation -ne $f0) 'setup: tap on another own piece swaps them'

    # --- Settings modal (portrait setup) ---
    Invoke-Tap (Spot $s 'mobile.settings'); $s = State
    Check ($s.settingsOpen -eq 'True' -and $s.modalTop -eq 'settings') 'tap 設定 opens the settings modal'
    Shot '02_portrait_settings.png'
    $before = Snap $s
    Invoke-Tap $a.At; Invoke-Tap (Spot $s 'mobile.omakase'); Invoke-TapXY 20 ([int](($s.screen -split 'x')[1]) - 20)
    $s = State
    Check ((Snap $s) -eq $before -and $s.settingsOpen -eq 'True') 'settings: taps on the board / おまかせ / outside do nothing, dialog stays open'
    Invoke-Tap (Spot $s 'mobile.settings.close'); $s = State
    Check ($s.settingsOpen -eq 'False' -and $s.formation -eq ($before -split '\|')[3]) 'settings: 閉じる closes it, nothing behind fired'

    # --- Presets: scroll the sheet, save to slot 2, change, load ---
    $h = [int](($s.screen -split 'x')[1]); $w = [int](($s.screen -split 'x')[0])
    & $adb shell input swipe ([int]($w / 2)) ([int]($h * 0.92)) ([int]($w / 2)) ([int]($h * 0.62)) 400 | Out-Null
    Start-Sleep -Milliseconds 500; $s = State
    Invoke-Tap (Spot $s 'preset.slot2'); $s = State
    Check ($s.presetSlot -eq '1') 'preset: slot 2 selected by tap (after scrolling the sheet by touch)'
    $saved = $s.formation
    Invoke-Tap (Spot $s 'preset.save'); $s = State
    Check ($s.presetSaving -eq 'True') 'preset: 保存 shows the name field'
    Start-Sleep 1; $s = State
    Check ((Get-Point (Spot $s 'preset.commit'))[1] -lt [int](($s.screen -split 'x')[1])) 'preset: the sheet scrolls so 保存 is visible'
    Invoke-Tap (Spot $s 'preset.name'); Start-Sleep 1
    $ime = @(& $adb shell dumpsys input_method | Select-String 'mInputShown=true').Count -gt 0
    Check $ime 'preset name: tapping the name field opens the software keyboard'
    Shot '03_portrait_preset_keyboard.png'
    & $adb shell input text 'AT' | Out-Null
    & $adb shell input keyevent 66 | Out-Null          # Enter: confirm on the keyboard
    Start-Sleep 1
    $ime = @(& $adb shell dumpsys input_method | Select-String 'mInputShown=true').Count -gt 0
    if ($ime) { & $adb shell input keyevent 111 | Out-Null; Start-Sleep 1 }   # Esc hides the keyboard if still shown
    $s = State
    Shot '03b_portrait_preset_name.png'
    Invoke-Tap (Spot $s 'preset.commit'); $s = State
    $name = ($s.presets -split '\|')[1]
    Check ($s.presetSaving -eq 'False' -and $name -ne 'プリセット2' -and $name -notlike '*(empty)*') "preset: saved with the name typed on the keyboard ($name)"
    Invoke-Tap (Spot $s 'mobile.omakase'); $s = State
    Check ($s.formation -ne $saved) 'おまかせ配置 changes the placement'
    Invoke-Tap (Spot $s 'preset.load'); $s = State
    Check ($s.formation -eq $saved) 'preset: 呼び出し restores the saved placement'

    # --- Start and play by taps ---
    & $adb shell input swipe ([int]($w / 2)) ([int]($h * 0.62)) ([int]($w / 2)) ([int]($h * 0.95)) 300 | Out-Null
    Start-Sleep -Milliseconds 500; $s = State
    Invoke-Tap (Spot $s 'mobile.start'); $s = WaitFor { param($x) $x.phase -eq 'PlayerTurn' }
    Check ($s.phase -eq 'PlayerTurn') 'tap 対局開始 starts the game'
    $moved = $false
    foreach ($p in ($s.own -split ';')) {
        $k, $at = $p -split '@'
        Invoke-Tap $at; $s = State
        if ($s.targets) { $t = ($s.targets -split ';')[0]; $to = ($t -split '@')[1]; $ply = [int]$s.ply; Invoke-Tap $to; $s = State; $moved = [int]$s.ply -gt $ply; break }
    }
    Check $moved 'tap own piece → tap target moves it'
    $s = WaitFor { param($x) $x.phase -eq 'PlayerTurn' } 40
    Shot '04_portrait_play.png'

    # --- Enemy observation by tap ---
    $e = ($s.enemy -split ';')[0]; $eid = ($e -split ':')[0]; $eat = ($e -split '@')[1]
    Invoke-Tap $eat; $s = State
    Check ($s.inspected -eq $eid -and $s.tooltip -like 'Enemy #*' -and $s.tooltip -notmatch '真値|%') "tap enemy piece shows its public observations ($($s.tooltip))"
    Shot '05_portrait_enemy_info.png'
    $s = State
    $e2 = @($s.enemy -split ';' | Where-Object { ($_ -split ':')[0] -ne $eid -and [int](($_ -split '@')[1] -split ',')[1] -lt [int]($eat -split ',')[1] + 5 }) | Select-Object -Last 1
    Invoke-Tap (($e2 -split '@')[1]); $s = State
    Check ($s.inspected -eq ($e2 -split ':')[0]) "tap another enemy piece switches the target ($e2 → inspected $($s.inspected))"
    $sa = $s.safe -split ','
    Invoke-TapXY ([int]$sa[0] + 10) ([int](([int]($s.camera -split ',')[1]) + 8)); $s = State
    Check ($s.inspected -eq '-1') 'tap outside the pieces closes the observations'

    # --- Help modal: pause, clock, no tap-through ---
    Invoke-Tap (Spot $s 'mobile.help'); $s = State
    $c0 = [double]$s.clock; $before = Snap $s
    Check ($s.helpOpen -eq 'True' -and $s.paused -match 'Help') 'tap あそびかた opens help and pauses'
    Shot '06_portrait_help.png'
    Invoke-Tap $eat; Start-Sleep 1; $s = State
    Check ((Snap $s) -eq $before -and [double]$s.clock -eq $c0) 'help: taps behind do nothing, clock stopped'
    & $adb shell input swipe ([int]($w / 2)) ([int]($h * 0.8)) ([int]($w / 2)) ([int]($h * 0.3)) 400 | Out-Null
    Start-Sleep -Milliseconds 500; $s = State
    Shot '07_portrait_help_scrolled.png'
    Invoke-Tap (Spot $s 'help.close'); Start-Sleep 1; $s = State
    Check ($s.helpOpen -eq 'False' -and [double]$s.clock -gt $c0) 'help: 閉じる closes it and the clock runs again'

    # --- Rotation: landscape and back, same game ---
    $fp = $s.fingerprint; $session = $s.session; $cpu = $s.cpu; $rnd = $s.random
    $null = Send-Remote 'orient' 'landscape'; Start-Sleep 3; $s = State
    Check ($s.portrait -eq 'False' -and $s.screen -match '^2340x' -or $s.portrait -eq 'False') "landscape: the app rotated ($($s.screen))"
    Check ($s.graves3d -eq 'True' -and $s.fingerprint -eq $fp -and $s.session -eq $session -and $s.cpu -eq $cpu -and $s.random -eq $rnd) 'landscape: same session, CPU, random state and fingerprint; 3D loss tables'
    $spots = $s.Keys | Where-Object { $_ -like 'spot.mobile.*' -and $_ -notlike '*sheet*' -and $_ -notlike '*start*' -and $_ -notlike '*omakase*' -and $_ -notlike '*result*' -and $_ -notlike '*confirm*' }
    Check (@($spots | Where-Object { -not (InSafe $s $s[$_]) }).Count -eq 0) 'landscape: header buttons inside the safe area'
    Shot '08_landscape_play.png'
    Invoke-Tap $s['spot.mobile.settings']; $s = State
    Check ($s.settingsOpen -eq 'True') 'landscape: 設定 opens'
    Shot '09_landscape_settings.png'
    Invoke-Tap $s['spot.mobile.settings.close']; $s = State
    Check ($s.settingsOpen -eq 'False') 'landscape: the always-visible 閉じる closes settings'
    $null = Send-Remote 'orient' 'portrait'; Start-Sleep 3; $s = State
    Check ($s.portrait -eq 'True' -and $s.fingerprint -eq $fp -and $s.session -eq $session) 'portrait again: same game'

    # --- Lifecycle: Home, then back ---
    $s = State; $c0 = [double]$s.clock; $bg = [int]$s.backgroundCount; $ply = $s.ply; $t0 = [DateTime]::Now
    & $adb shell input keyevent KEYCODE_HOME; Start-Sleep 5
    & $adb shell am start -n $activity | Out-Null; Start-Sleep 3
    $s = State; $real = ([DateTime]::Now - $t0).TotalSeconds
    Check ([int]$s.backgroundCount -gt $bg -and $s.session -eq $session -and $s.background -eq 'False') "Home → back: paused in the background, resumed the same session (background count $($s.backgroundCount))"
    Check (($real - ([double]$s.clock - $c0)) -ge 4.5) ("clock stopped in the background: +{0:0.00} s of game time over {1:0.0} s real time with ~5 s at the home screen" -f ([double]$s.clock - $c0), $real)

    # --- Sound ---
    $s = Send-Remote 'audio'
    Check ([double]$s.audioPeak -gt 0.02) "sfx_select reaches the output mix on the device (peak $($s.audioPeak))"

    # --- Referee notice and result modals (real taps) ---
    $s = Send-Remote 'judge'; $s = WaitFor { param($x) $x.judgeOpen -eq 'True' } 10
    $before = Snap $s; $c0 = [double]$s.clock
    Shot '10_judge_notice.png'
    # Behind = outside the notice panel: a board piece below it, the 設定 button, the top bar.
    $tl = Get-Point $s['spot.judge.panelTL']; $br = Get-Point $s['spot.judge.panelBR']
    $under = @($s.own -split ';' | ForEach-Object { ($_ -split '@')[1] } | Where-Object { (Get-Point $_)[1] -gt $br[1] + 10 }) | Select-Object -First 1
    if ($under) { Invoke-Tap $under }
    Invoke-Tap $s['spot.mobile.settings']; Invoke-TapXY 100 ($tl[1] - 40); Start-Sleep 1; $s = State
    Check ($s.judgeOpen -eq 'True' -and (Snap $s) -eq $before -and [double]$s.clock -eq $c0) "referee notice: taps outside it (board $under, 設定, top) do nothing; game and clock paused"
    Invoke-Tap $s['spot.judge.continue']; $s = State
    Check ($s.judgeOpen -eq 'False' -and $s.settingsOpen -eq 'False') '続行 closes only the notice'
    Invoke-Tap $s['spot.mobile.new']; $s = State
    Check ($s.modalTop -eq 'confirmNew') '新規対局 asks for confirmation during a game'
    Invoke-Tap $s['spot.mobile.confirm.new']; $s = WaitFor { param($x) $x.phase -eq 'Setup' } 10
    Invoke-Tap $s['spot.mobile.start']; $s = WaitFor { param($x) $x.phase -eq 'PlayerTurn' } 20
    $s = Send-Remote 'judge'; $s = WaitFor { param($x) $x.judgeOpen -eq 'True' } 10
    Invoke-Tap $s['spot.judge.resign']; $s = WaitFor { param($x) $x.resultOpen -eq 'True' } 10
    Check ($s.resultReason -eq 'Resigned' -and $s.result -like 'CPUの勝ち*投了*') "投了 → result: $($s.result)"
    Shot '11_result_resigned.png'
    $before = Snap $s
    Invoke-Tap $s['spot.mobile.settings']; $s = State
    Check ($s.settingsOpen -eq 'False' -and $s.resultOpen -eq 'True') 'result: taps behind do nothing'
    Invoke-Tap $s['spot.mobile.result.board']; $s = State
    Check ($s.resultOpen -eq 'False' -and $s.modalTop -eq 'none') '盤面を見る closes the result'

    # --- Determinism with rotation: same seeds as the PC emulation, rotate every other ply ---
    $s = Send-Remote 'seeds' '1001,2002,3003'
    $s = State; Invoke-Tap $s['spot.mobile.start']; $s = WaitFor { param($x) $x.phase -eq 'PlayerTurn' } 20
    for ($i = 0; $i -lt 14; $i++) {
        $null = Send-Remote 'automove'
        if ($i % 2 -eq 0) { $null = Send-Remote 'orient' ($(if ($i % 4 -eq 0) { 'landscape' } else { 'portrait' })) }
        $s = WaitFor { param($x) $x.phase -eq 'PlayerTurn' -or $x.phase -eq 'Finished' } 60
    }
    $null = Send-Remote 'orient' 'portrait'
    $log.Add("ply $($s.ply) fingerprint $($s.fingerprint) (PC emulation without rotation, same seeds: 8DE501F9C87C5300 at ply 28)")
    Check ($s.ply -eq '28' -and $s.fingerprint -eq '8DE501F9C87C5300') 'rotating during a game changes nothing: device fingerprint = PC fingerprint'
    Check ([int]$s.uiErrors -eq 0) "no UI exceptions ($($s.uiErrors))"
} finally {
    try { $null = Send-Remote 'orient' 'auto' 10 } catch { }
    & $adb shell settings put system user_rotation $rotUser | Out-Null
    & $adb shell settings put system accelerometer_rotation $rotAuto | Out-Null
    $log.Add($(if ($failures -eq 0) { 'PASS' } else { "FAIL ($failures)" }))
    $log | Set-Content -LiteralPath (Join-Path $OutDir 'android_test_report.txt') -Encoding utf8
    Write-Host "Report: $OutDir"
}
if ($failures) { throw "Android device test failed ($failures)" }
