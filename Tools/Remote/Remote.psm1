# Host side of RemoteControl.cs (the -remote test harness). Two targets:
#   PC   : AF-MilitaryShogi.exe -remote -dataDir <dir> [-mobileui]  → files in <dir>\remote
#   Device: app launched with  -e args "-remote"                     → files in the app's external files dir
# Taps on the device are real touches (adb shell input tap). The file channel only reads state and
# triggers test-only actions (see RemoteControl.cs).
$script:Seq = 0
$script:Target = $null

function Connect-RemotePc([string]$DataDir) {
    $script:Target = @{ Kind = 'pc'; Dir = (Join-Path $DataDir 'remote') }
}

function Connect-RemoteDevice([string]$Adb, [string]$Package = 'com.af.militaryshogi') {
    $script:Target = @{ Kind = 'adb'; Adb = $Adb; Dir = "/sdcard/Android/data/$Package/files/remote" }
}

function Read-RemoteText([string]$Name) {
    if ($script:Target.Kind -eq 'pc') {
        $p = Join-Path $script:Target.Dir $Name
        if (Test-Path -LiteralPath $p) { return [IO.File]::ReadAllText($p) } else { return $null }
    }
    $t = & $script:Target.Adb shell "cat $($script:Target.Dir)/$Name 2>/dev/null"
    if ($LASTEXITCODE -ne 0 -or -not $t) { return $null }
    return ($t -join "`n")
}

function ConvertFrom-RemoteState([string]$Text) {
    $h = @{}
    foreach ($line in ($Text -split "`n")) {
        $i = $line.IndexOf('=')
        if ($i -gt 0) { $h[$line.Substring(0, $i)] = $line.Substring($i + 1).TrimEnd("`r") }
    }
    return $h
}

function Get-RemoteState { return ConvertFrom-RemoteState (Read-RemoteText 'state.txt') }

function Send-Remote([string]$Command, [string]$Arg = '', [int]$TimeoutSec = 30) {
    $script:Seq++
    $line = "$($script:Seq) $Command $Arg".Trim()
    if ($script:Target.Kind -eq 'pc') {
        [IO.File]::WriteAllText((Join-Path $script:Target.Dir 'cmd.txt'), $line)
    } else {
        & $script:Target.Adb shell "echo '$line' > $($script:Target.Dir)/cmd.txt" | Out-Null
    }
    $deadline = [DateTime]::Now.AddSeconds($TimeoutSec)
    while ([DateTime]::Now -lt $deadline) {
        $text = Read-RemoteText 'state.txt'
        if ($text) {
            $s = ConvertFrom-RemoteState $text
            if ($s['seq'] -eq "$($script:Seq)") { return $s }
        }
        Start-Sleep -Milliseconds 150
    }
    throw "Remote command timed out: $line"
}

function Get-Point([string]$Value) {
    $p = $Value -split ','
    return @([int]$p[0], [int]$p[1])
}

function Invoke-Tap([string]$Point) {
    $p = Get-Point $Point
    & $script:Target.Adb shell input tap $p[0] $p[1] | Out-Null
    Start-Sleep -Milliseconds 350
}

function Invoke-TapXY([int]$X, [int]$Y) {
    & $script:Target.Adb shell input tap $X $Y | Out-Null
    Start-Sleep -Milliseconds 350
}

Export-ModuleMember -Function Connect-RemotePc, Connect-RemoteDevice, Get-RemoteState, Send-Remote, Get-Point, Invoke-Tap, Invoke-TapXY, Read-RemoteText
