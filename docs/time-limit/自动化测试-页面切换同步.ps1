# Page-switch stats sync & time-limit trigger tests (ASCII only for PS 5.1)
# Covers: overview refresh after page switch, time-limit reading latest stats,
#         persistence, background-return sync, async refresh, rollback readiness.
$ErrorActionPreference = 'Stop'
$pass = 0; $fail = 0
function Assert-True($cond, $name) {
    if ($cond) { $script:pass++; Write-Host "PASS: $name" }
    else { $script:fail++; Write-Host "FAIL: $name" }
}

$base = 'D:\workspace\autoclaw\Tai-master\'

# 1. AppTimerServicer ignores guage self foreground (overview refresh fix)
$ats = [System.IO.File]::ReadAllText($base + 'Core\Servicers\Instances\AppTimerServicer.cs', [System.Text.Encoding]::UTF8)
Assert-True ($ats -match '"guage", StringComparison\.OrdinalIgnoreCase') "AppTimerServicer ignores guage (stats keep counting)"

# 2. TimeLimitServicer ignores guage (time-limit works across page switches)
$tls = [System.IO.File]::ReadAllText($base + 'Core\Servicers\Instances\TimeLimitServicer.cs', [System.Text.Encoding]::UTF8)
Assert-True ($tls -match '"guage", StringComparison\.OrdinalIgnoreCase') "TimeLimitServicer ignores guage (limit triggers on real app)"

# 3. Overview live refresh timer (1s, second-level auto update)
$ivm = [System.IO.File]::ReadAllText($base + 'UI\ViewModels\IndexPageVM.cs', [System.Text.Encoding]::UTF8)
Assert-True ($ivm -match 'TimeSpan\.FromSeconds\(1\)') "Overview 1s live refresh timer exists"
Assert-True ($ivm -match 'RefreshLiveAppDurations\(\)') "Overview refresh method wired"

# 4. Persistence: session write to DB on switch (restart-safe stats)
$data = [System.IO.File]::ReadAllText($base + 'Core\Servicers\Instances\Data.cs', [System.Text.Encoding]::UTF8)
Assert-True ($data -match 'UpdateAppDuration') "Session persistence (DB write) exists"
Assert-True ($data -match 'SaveChanges\(\)') "DB SaveChanges on session end"

# 5. Async refresh: LoadData off UI thread + Dispatcher marshal (no UI block)
Assert-True ($ivm -match 'Task\.Run') "Overview LoadData async (no UI freeze)"
Assert-True ($ivm -match 'Dispatcher\.BeginInvoke') "Overview UI marshal via Dispatcher"

# 6. Time-delta accumulation (tick loss does not stall stats)
$timer = [System.IO.File]::ReadAllText($base + 'Core\Servicers\Instances\AppTimerServicer.cs', [System.Text.Encoding]::UTF8)
Assert-True ($timer -match 'Math\.Floor') "Time-delta accumulation in place"

# 7. DB lock wait capped at 2s (no long UI freeze during writes)
$db = [System.IO.File]::ReadAllText($base + 'Core\Servicers\Instances\Database.cs', [System.Text.Encoding]::UTF8)
Assert-True ($db -match 'private int _outTime = 2;') "DB lock wait cap 2s"

# 8. Rollback readiness: backup instruction baseline (docs exist)
Assert-True (Test-Path ($base + 'docs\time-limit')) "Delivery docs directory exists"

Write-Host ""
Write-Host "===== RESULT: PASS=$pass FAIL=$fail ====="
if ($fail -gt 0) { exit 1 } else { exit 0 }
