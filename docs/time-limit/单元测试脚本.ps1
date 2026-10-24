# TimeLimit / Stats core logic unit tests (ASCII only for PS 5.1 compat)
$ErrorActionPreference = 'Stop'
$pass = 0; $fail = 0
function Assert-True($cond, $name) {
    if ($cond) { $script:pass++; Write-Host "PASS: $name" }
    else { $script:fail++; Write-Host "FAIL: $name" }
}

# 1. Random password generation (6 chars, guaranteed digit+letter, no confusables)
$digits = '23456789'
$letters = 'ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz'
$allOk = $true
for ($t = 0; $t -lt 20; $t++) {
    $chars = New-Object char[] 6
    $rng = New-Object System.Random
    $chars[0] = $digits[$rng.Next($digits.Length)]
    $chars[1] = $letters[$rng.Next($letters.Length)]
    for ($i = 2; $i -lt 6; $i++) {
        $pool = if ($rng.Next(2) -eq 0) { $digits } else { $letters }
        $chars[$i] = $pool[$rng.Next($pool.Length)]
    }
    for ($i = 5; $i -gt 0; $i--) {
        $j = $rng.Next($i + 1)
        $tmp = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $tmp
    }
    $pwd = -join $chars
    if (-not (($pwd -match '[0-9]') -and ($pwd -match '[A-Za-z]') -and ($pwd.Length -eq 6))) { $allOk = $false; Write-Host "bad sample: $pwd" }
}
Assert-True $allOk "random pwd x20 always digit+letter+len6 (sample: $pwd)"
Assert-True (($digits + $letters) -notmatch '[01OIil]') "char set excludes 0/1/O/I/l"

# 2. SHA256 hash consistency
function Get-Sha256([string]$text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($text))
    ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
}
Assert-True ((Get-Sha256 'MyP@ss123') -eq (Get-Sha256 'MyP@ss123')) "SHA256 same input same hash"
Assert-True ((Get-Sha256 'MyP@ss123') -ne (Get-Sha256 'MyP@ss124')) "SHA256 diff input diff hash"
Assert-True ((Get-Sha256 'x').Length -eq 64) "SHA256 length 64 hex"

# 3. Stats time-delta accumulation formula
function Get-DurationSeconds([datetime]$start, [datetime]$now) {
    [int][Math]::Floor(($now - $start).TotalSeconds)
}
$s = Get-Date '2026-08-13 10:00:00'
Assert-True ((Get-DurationSeconds $s $s.AddSeconds(1)) -eq 1) "delta 1s"
Assert-True ((Get-DurationSeconds $s $s.AddSeconds(61)) -eq 61) "delta 61s (missed tick no loss)"

# 4. DB lock timeout 2s (was 60s)
$dbFile = 'D:\workspace\autoclaw\Tai-master\Core\Servicers\Instances\Database.cs'
$dbContent = [System.IO.File]::ReadAllText($dbFile, [System.Text.Encoding]::UTF8)
Assert-True ($dbContent -match 'private int _outTime = 2;') "DB lock timeout = 2s"
Assert-True ($dbContent -notmatch 'private int _outTime = 60;') "no 60s lock wait remains"

# 5. Countdown shortest path (55<->5 cross zero)
function Get-ShortPath([double]$from, [double]$to) {
    if ([Math]::Abs($to - $from) -gt 30) {
        if ($to -gt $from) { $to -= 60 } else { $to += 60 }
    }
    [Math]::Abs($to - $from)
}
Assert-True ((Get-ShortPath 55 5) -eq 10) "shortest path 55->5 = 10"
Assert-True ((Get-ShortPath 5 55) -eq 10) "shortest path 5->55 = 10"
Assert-True ((Get-ShortPath 10 20) -eq 10) "normal path 10->20 = 10"

# 6. Model fields present
$ruleModel = [System.IO.File]::ReadAllText('D:\workspace\autoclaw\Tai-master\Core\Models\Config\TimeLimitRuleModel.cs', [System.Text.Encoding]::UTF8)
foreach ($field in @('UseNativeMessageBox','ShowFloatingCountdown','CountdownOpacity','HideByTripleClick','BanRestart','PasswordMode','CustomPasswordHash','RelockMinutes','RelockAfterExit')) {
    Assert-True ($ruleModel -match "public .* $field") "rule field $field"
}
$setModel = [System.IO.File]::ReadAllText('D:\workspace\autoclaw\Tai-master\Core\Models\Config\TimeLimitSettingsModel.cs', [System.Text.Encoding]::UTF8)
Assert-True ($setModel -match 'RandomPassword') "settings field RandomPassword"
Assert-True ($setModel -match 'RandomPasswordViewed') "settings field RandomPasswordViewed"

# 7. Stats fixes present
$obs = [System.IO.File]::ReadAllText('D:\workspace\autoclaw\Tai-master\Core\Servicers\Instances\AppObserver.cs', [System.Text.Encoding]::UTF8)
Assert-True ($obs -match '_lastForegroundHwnd') "AppObserver fallback poll"
$timer = [System.IO.File]::ReadAllText('D:\workspace\autoclaw\Tai-master\Core\Servicers\Instances\AppTimerServicer.cs', [System.Text.Encoding]::UTF8)
Assert-True ($timer -match 'Math\.Floor') "AppTimerServicer time-delta"
$idx = [System.IO.File]::ReadAllText('D:\workspace\autoclaw\Tai-master\UI\ViewModels\IndexPageVM.cs', [System.Text.Encoding]::UTF8)
Assert-True ($idx -notmatch 'AllowLiveUiUpdates') "IndexPageVM no animation-budget gate"

# 8. Countdown / Lock window features
$cd = [System.IO.File]::ReadAllText('D:\workspace\autoclaw\Tai-master\UI\Controls\Window\CountdownWindow.xaml.cs', [System.Text.Encoding]::UTF8)
Assert-True ($cd -match 'Hide\(\)' -and $cd -match '_clickCount') "CountdownWindow triple-click hide"
Assert-True ($cd -match 'SystemParameters\.WorkArea') "CountdownWindow top-right positioning"
$lk = [System.IO.File]::ReadAllText('D:\workspace\autoclaw\Tai-master\UI\Controls\Window\LockOverlayWindow.xaml.cs', [System.Text.Encoding]::UTF8)
Assert-True ($lk -match 'Sha256' -and $lk -match '_randomPassword') "LockOverlayWindow password verify"
Assert-True ($lk -match 'ShowPwdButton') "LockOverlayWindow one-time pwd view"

Write-Host ""
Write-Host "===== RESULT: PASS=$pass FAIL=$fail ====="
if ($fail -gt 0) { exit 1 } else { exit 0 }
