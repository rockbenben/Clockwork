$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '_assert.ps1')
. (Join-Path $here '..\lib\StartupHelper.Core.ps1')

Write-Host 'Get-DefaultConfig (launchSteps)'
$cfg = Get-DefaultConfig
Assert-True  ($cfg.launchSteps.Count -ge 10)        'default has launch steps'
Assert-True  ($cfg.reminders.Count -ge 1)           'default has reminders'
Assert-Equal 'volume' $cfg.launchSteps[0].kind      'first step is the mute(volume) step'
Assert-True  ([bool]$cfg.launchSteps[0].onlyBefore8) 'mute step is onlyBefore8'

Write-Host 'round-trip'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("sh_t_{0}.json" -f $PID)
Write-Config $cfg $tmp
$back = Read-Config $tmp
Assert-Equal $cfg.launchSteps.Count $back.launchSteps.Count 'round-trip step count'
Assert-Equal $cfg.launchSteps[1].label $back.launchSteps[1].label 'round-trip app label'
Remove-Item $tmp -ErrorAction SilentlyContinue

Write-Host 'Read-Config missing -> defaults'
$miss = Join-Path ([System.IO.Path]::GetTempPath()) ("sh_none_{0}.json" -f $PID)
$d = Read-Config $miss
Assert-True ($d.launchSteps.Count -ge 10) 'missing -> default steps'

Write-Host 'ConvertTo-LaunchSteps (migration)'
$old = [pscustomobject]@{
  launchItems = @(
    [pscustomobject]@{ name='A'; target='a.exe'; args=''; workDir=''; elevated=$false; delayMs=5; enabled=$true },
    [pscustomobject]@{ name='B'; target='b.exe'; args=''; workDir=''; elevated=$true;  delayMs=0; enabled=$false })
  specialSteps = [pscustomobject]@{ muteBefore8=$true; wechatAutoLogin=$true; closeTIM=$true; closeQQ=$false; minimizeThunderbird=$true; extraSendKeys=@('Win+6','Alt+K') }
}
$st = ConvertTo-LaunchSteps $old
Assert-Equal 'volume' $st[0].kind     'migration: mute first'
Assert-Equal 'app'    $st[1].kind     'migration: app A'
Assert-Equal 'A'      $st[1].label    'migration: label from name'
Assert-Equal $false   ([bool]$st[2].enabled) 'migration: app B keeps disabled'
$logins = @($st | Where-Object { $_.kind -eq 'window' -and $_.action -eq 'sendkey' })
Assert-Equal 1 $logins.Count 'one wechat login'
$closes = @($st | Where-Object { $_.kind -eq 'window' -and $_.action -eq 'close' })
Assert-Equal 1 $closes.Count 'closeTIM only (closeQQ false)'
$keys = @($st | Where-Object { $_.kind -eq 'keys' })
Assert-Equal 2 $keys.Count 'two keys steps'
Assert-Equal 'keys' $st[-1].kind 'keys appended last'

Write-Host 'Read-Config migrates old file (drops old keys)'
$oldFile = Join-Path ([System.IO.Path]::GetTempPath()) ("sh_old_{0}.json" -f $PID)
($old | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $oldFile -Encoding UTF8
$mig = Read-Config $oldFile
Assert-True ($null -ne $mig.launchSteps) 'migrated has launchSteps'
Assert-True ($null -eq $mig.launchItems) 'old launchItems dropped'
Remove-Item $oldFile -ErrorAction SilentlyContinue

Write-Host 'Read-Config backfills missing days (pre-days configs)'
# days 特性之前的配置：步骤/提醒没有 days 字段。缺失时 @($null).Count=1 会让 Build-LaunchPlan 把
# 每个步骤都当成「有星期限制且今天不匹配」而跳过 → 启动清单什么都不启动。Read-Config 必须补成空数组。
$preDays = [pscustomobject]@{
  launchSteps = @(
    [pscustomobject]@{ enabled=$true; kind='app'; label='X'; target='x.exe'; delayMs=0 }
    [pscustomobject]@{ enabled=$true; kind='keys'; label='Win+6'; combo='Win+6'; delayMs=0 })
  reminders = @([pscustomobject]@{ time='09:00'; message='m'; speak=$false; confirm=$true; onYes=@{type='none';target=''}; enabled=$true })
  settings  = [pscustomobject]@{ tickSeconds=30 }
}
$preFile = Join-Path ([System.IO.Path]::GetTempPath()) ("sh_predays_{0}.json" -f $PID)
($preDays | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $preFile -Encoding UTF8
$healed = Read-Config $preFile
Assert-True ($null -ne $healed.launchSteps[0].PSObject.Properties['days']) 'step days backfilled'
Assert-Equal 0 (@($healed.launchSteps[0].days)).Count 'backfilled days is empty (=every day)'
Assert-True ($null -ne $healed.reminders[0].PSObject.Properties['days']) 'reminder days backfilled'
Assert-True ($null -eq $healed.reminders[0].PSObject.Properties['confirm']) '废弃字段 confirm 被 Read-Config 清除'
$healedPlan = Build-LaunchPlan $healed 10 3
Assert-Equal 2 $healedPlan.Count 'pre-days steps all run after heal (not skipped)'
$mon0900b = Get-Date '2026-06-29 09:00:00'
Assert-Equal 'arm' (Get-ReminderDecision $healed.reminders[0] $mon0900b $mon0900b (New-ReminderState)).action 'pre-days reminder armable after heal'
Remove-Item $preFile -ErrorAction SilentlyContinue

Write-Host 'Build-LaunchPlan (new model)'
$cfg2 = Get-DefaultConfig
$night = Build-LaunchPlan $cfg2 7
$day   = Build-LaunchPlan $cfg2 10
Assert-Equal 'volume' $night[0].kind            'before 8: mute included'
Assert-True  ($day[0].kind -ne 'volume')        'after 8: onlyBefore8 mute dropped'
Assert-Equal ($night.Count - 1) $day.Count      'day plan one shorter'
$cfg3 = Get-DefaultConfig; $cfg3.launchSteps[1].enabled = $false
$p3 = Build-LaunchPlan $cfg3 10
Assert-Equal ($day.Count - 1) $p3.Count          'disabled step excluded'
# 星期条件：days 非空时只在指定 ISO 日生效；空=每天（Build-LaunchPlan 用 ,$arr 返回，须直接赋值不可 @()包裹）
$cfg4 = Get-DefaultConfig; $cfg4.launchSteps[1].days = @(1,2,3,4,5)
$planMon = Build-LaunchPlan $cfg4 10 1
$planSun = Build-LaunchPlan $cfg4 10 7
Assert-True  ($planMon -contains $cfg4.launchSteps[1])    'weekday step included on Monday'
Assert-True  ($planSun -notcontains $cfg4.launchSteps[1]) 'weekday step excluded on Sunday'
Assert-True  ($planSun -contains $cfg4.launchSteps[2])    'empty-days step runs every day'

Write-Host 'Get-StepSummary'
Assert-Equal '设音量 50%'      (Get-StepSummary (New-LaunchStep 'volume' @{ action='set'; level=50 }))        'volume set'
Assert-Equal '静音（仅8点前）' (Get-StepSummary (New-LaunchStep 'volume' @{ action='mute'; onlyBefore8=$true })) 'mute before8'
Assert-Equal '关闭窗口 TIM'  (Get-StepSummary (New-LaunchStep 'window' @{ action='close'; process='TIM' }))  'window close'
Assert-Equal '发送 Win+6'      (Get-StepSummary (New-LaunchStep 'keys'   @{ combo='Win+6' }))                  'keys'
Assert-Equal '锁屏（回来需输密码）' (Get-StepSummary (New-LaunchStep 'system' @{ command='lockScreen' }))    'system'

Write-Host 'Get-StepKindLabel'
Assert-Equal '启动程序' (Get-StepKindLabel 'app')    'kind app'
Assert-Equal '系统命令' (Get-StepKindLabel 'system') 'kind system'

Write-Host 'ConvertFrom-KeyCombo'
$k1 = ConvertFrom-KeyCombo 'Win+6'
Assert-True  $k1.UseWin     'Win+6 UseWin'
Assert-Equal '6' $k1.Key    'Win+6 key'
$k2 = ConvertFrom-KeyCombo 'Alt+K'
Assert-True  ($k2.Modifiers -contains 'Alt') 'Alt+K has Alt'
Assert-Equal 'K' $k2.Key    'Alt+K key'

Write-Host 'ConvertTo-SendKeysString (大写字母不得多带 Shift)'
Assert-Equal '%k'  (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Alt+K'))        'Alt+K -> %k（不是 %K=Alt+Shift+K）'
Assert-Equal '^+m' (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Ctrl+Shift+M')) 'Ctrl+Shift+M -> ^+m'
Assert-Equal '6'   (ConvertTo-SendKeysString (ConvertFrom-KeyCombo '6'))            'digit 不变'
Assert-Equal '%{F4}' (ConvertTo-SendKeysString ([pscustomobject]@{ Modifiers=@('Alt'); Key='{F4}'; UseWin=$false })) '非单字符键原样保留'
Assert-Equal '%{F4}'  (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Alt+F4'))   '裸 F 键自动加花括号 Alt+F4 -> %{F4}'
Assert-Equal '^{F12}' (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Ctrl+F12')) 'Ctrl+F12 -> ^{F12}'
Assert-True ($null -eq (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Ctrl+Bogus'))) '未知键名返回 null（拒发，防 ^Bogus 注入文字）'

Write-Host 'ConvertTo-SendKeysSequence（窗口发送按键的宽容解析）'
Assert-Equal '{ENTER}'    (ConvertTo-SendKeysSequence 'Enter')       'Enter -> {ENTER}'
Assert-Equal '^{ENTER}'   (ConvertTo-SendKeysSequence 'Ctrl+Enter')  'Ctrl+Enter -> ^{ENTER}'
Assert-Equal '{ENTER}'    (ConvertTo-SendKeysSequence '{ENTER}')     '原生序列原样'
Assert-Equal 'hello{TAB}' (ConvertTo-SendKeysSequence 'hello{TAB}')  '含花括号的混合序列原样'
Assert-Equal 'hello'      (ConvertTo-SendKeysSequence 'hello')       '识别不了的裸词退回原样（当字面文本）'
Assert-Equal '{F5}'       (ConvertTo-SendKeysSequence 'F5')          'F5 -> {F5}'

Write-Host 'New-Reminder 默认字段'
$nr = New-Reminder
Assert-Equal 'time' $nr.trigger            'trigger 默认 time'
Assert-Equal 5  $nr.graceMinutes           'graceMinutes 默认 5'
Assert-Equal 0  $nr.delaySeconds           'delaySeconds 默认 0'
Assert-Equal 0  $nr.randomDelaySeconds     'randomDelaySeconds 默认 0'
Assert-Equal 0  $nr.repeatMinutes          'repeatMinutes 默认 0'
Assert-Equal '' $nr.repeatUntil            'repeatUntil 默认空'
Assert-Equal 'startup' (New-Reminder @{trigger='startup'}).trigger 'Props 覆盖 trigger'

Write-Host 'Read-Config 给旧提醒补新字段'
$tmp = Join-Path $env:TEMP ("rc_rem_" + [guid]::NewGuid().ToString('N') + '.json')
'{"reminders":[{"time":"08:00","days":[1,2,3],"message":"x","speak":false,"confirm":true,"onYes":{"type":"none","target":""},"enabled":true}]}' | Set-Content -LiteralPath $tmp -Encoding UTF8
$rc = Read-Config $tmp
Remove-Item -LiteralPath $tmp -Force
Assert-Equal 'time' $rc.reminders[0].trigger       '补 trigger=time'
Assert-Equal 5  $rc.reminders[0].graceMinutes      '补 graceMinutes=5'
Assert-Equal 0  $rc.reminders[0].repeatMinutes     '补 repeatMinutes=0'
Assert-Equal '' $rc.reminders[0].repeatUntil       '补 repeatUntil 空'
Assert-Equal '08:00' $rc.reminders[0].time         '原 time 不变'

Write-Host 'Get-ReminderDecision'
$start = [datetime]'2026-06-30 08:00'
$now0  = [datetime]'2026-06-30 09:00'
$iso   = [int]$now0.DayOfWeek; if ($iso -eq 0) { $iso = 7 }

# 准点 → arm；arm 后置 pendingFireAt 再调用 → fire；fire 后当天不再 arm
$r = New-Reminder @{ time='09:00'; days=@(); graceMinutes=5 }
$st = New-ReminderState
$d = Get-ReminderDecision $r $now0 $start $st
Assert-Equal 'arm' $d.action 'on-time -> arm'
Assert-Equal $now0 $d.base   'arm base = today@time'
$st.pendingFireAt = $now0           # GUI 无延迟时直接置为 base
$d = Get-ReminderDecision $r $now0 $start $st
Assert-Equal 'fire' $d.action 'pendingFireAt 到点 -> fire'
Assert-Equal '2026-06-30' $st.lastFiredDate 'fire 置 lastFiredDate'
$d = Get-ReminderDecision $r ([datetime]'2026-06-30 09:01') $start $st
Assert-Equal 'none' $d.action '当天已发不再 arm'

# 错过整分但在 grace 内 → arm；超过 grace → none
$st2 = New-ReminderState
Assert-Equal 'arm'  (Get-ReminderDecision (New-Reminder @{time='09:00';graceMinutes=5}) ([datetime]'2026-06-30 09:04') $start $st2).action 'grace 内补发'
$st3 = New-ReminderState
Assert-Equal 'none' (Get-ReminderDecision (New-Reminder @{time='09:00';graceMinutes=5}) ([datetime]'2026-06-30 09:06') $start $st3).action 'grace 外不发'

# grace=0 退化为精确整分
$st4 = New-ReminderState
Assert-Equal 'none' (Get-ReminderDecision (New-Reminder @{time='09:00';graceMinutes=0}) ([datetime]'2026-06-30 09:01') $start $st4).action 'grace=0 精确整分'
$stG0 = New-ReminderState
Assert-Equal 'arm' (Get-ReminderDecision (New-Reminder @{time='09:00';graceMinutes=0}) ([datetime]'2026-06-30 09:00:30') $start $stG0).action 'grace=0 整分内(含秒)仍 arm'

# 星期过滤（用 $iso 推导，避免硬编码星期）
$stIn  = New-ReminderState
Assert-Equal 'arm'  (Get-ReminderDecision (New-Reminder @{time='09:00';days=@($iso)}) $now0 $start $stIn).action  '今天在 days 内 -> arm'
$stOut = New-ReminderState
$other = ($iso % 7) + 1
Assert-Equal 'none' (Get-ReminderDecision (New-Reminder @{time='09:00';days=@($other)}) $now0 $start $stOut).action '今天不在 days -> none'

# startup 触发：StartTime 后 arm 一次；同次运行不再
$rs = New-Reminder @{ trigger='startup'; days=@() }
$sts = New-ReminderState
$d = Get-ReminderDecision $rs $now0 $start $sts
Assert-Equal 'arm' $d.action 'startup -> arm'
Assert-Equal $start $d.base  'startup base = StartTime'
$sts.pendingFireAt = $start
$d = Get-ReminderDecision $rs $now0 $start $sts
Assert-Equal 'fire' $d.action 'startup pendingFireAt 到点 -> fire'
Assert-True $sts.startupHandled 'fire 置 startupHandled'
Assert-Equal 'none' (Get-ReminderDecision $rs $now0 $start $sts).action 'startup 同次运行不再触发'

# 重复到点：nextRepeatAt 到点 -> fire 并清掉
$rr = New-Reminder @{ time='09:00' }
$str = New-ReminderState; $str.nextRepeatAt = [datetime]'2026-06-30 09:10'
$d = Get-ReminderDecision $rr ([datetime]'2026-06-30 09:10') $start $str
Assert-Equal 'fire' $d.action '重复到点 -> fire'
Assert-True ($null -eq $str.nextRepeatAt) 'fire 后清 nextRepeatAt'

# 禁用 -> none
Assert-Equal 'none' (Get-ReminderDecision (New-Reminder @{enabled=$false;time='09:00'}) $now0 $start (New-ReminderState)).action '禁用 -> none'

Write-Host 'Update-ReminderAfterFire'
$base = [datetime]'2026-06-30 09:00'
# 确认即停
$r = New-Reminder @{ repeatMinutes=10 }
$st = New-ReminderState; $st.nextRepeatAt=$base; $st.repeatCount=3
$st = Update-ReminderAfterFire $r $base 'ok' $st
Assert-True ($null -eq $st.nextRepeatAt) '确认 -> 清 nextRepeatAt'
Assert-Equal 0 $st.repeatCount '确认 -> repeatCount 归零'
# 未确认且开了重复 -> 排下次
$st3 = Update-ReminderAfterFire $r $base '' (New-ReminderState)
Assert-Equal ([datetime]'2026-06-30 09:10') $st3.nextRepeatAt '未确认 -> 10 分钟后重复'
Assert-Equal 1 $st3.repeatCount 'repeatCount++'
# repeatMinutes=0 -> 不重复
$st4 = Update-ReminderAfterFire (New-Reminder @{repeatMinutes=0}) $base '' (New-ReminderState)
Assert-True ($null -eq $st4.nextRepeatAt) 'repeatMinutes=0 -> 不排'
# 达安全上限 -> 停
$rMax = New-Reminder @{ repeatMinutes=10 }
$st5 = New-ReminderState; $st5.repeatCount = (Get-ReminderMaxRepeats) - 1
$st5 = Update-ReminderAfterFire $rMax $base '' $st5
Assert-True ($null -eq $st5.nextRepeatAt) '达上限 -> 停'
# 超过截止钟点 -> 停
$rUntil = New-Reminder @{ repeatMinutes=10; repeatUntil='09:05' }
$st6 = Update-ReminderAfterFire $rUntil $base '' (New-ReminderState)
Assert-True ($null -eq $st6.nextRepeatAt) '下次超过 repeatUntil -> 停'
# 截止钟点内 -> 继续
$rUntil2 = New-Reminder @{ repeatMinutes=10; repeatUntil='09:30' }
$st7 = Update-ReminderAfterFire $rUntil2 $base '' (New-ReminderState)
Assert-Equal ([datetime]'2026-06-30 09:10') $st7.nextRepeatAt 'repeatUntil 内 -> 继续'

Write-Host 'ConvertFrom-DurationText'
Assert-Equal 90   (ConvertFrom-DurationText '90')     '纯数字 90'
Assert-Equal 80   (ConvertFrom-DurationText '1h20m')  '1h20m=80'
Assert-Equal 120  (ConvertFrom-DurationText '2h')     '2h=120'
Assert-Equal 45   (ConvertFrom-DurationText '45m')    '45m=45'
Assert-Equal 60   (ConvertFrom-DurationText '1h')     '1h=60'
Assert-Equal 90   (ConvertFrom-DurationText '90m')    '90m=90'
Assert-Equal 80   (ConvertFrom-DurationText ' 1H 20M ') '大小写+空格容忍'
Assert-Equal $null (ConvertFrom-DurationText '')      '空 -> null'
Assert-Equal $null (ConvertFrom-DurationText 'abc')   '乱填 -> null'
Assert-Equal $null (ConvertFrom-DurationText '0')     '0 -> null(<1)'
Assert-Equal $null (ConvertFrom-DurationText '1h20')  '缺单位 -> null'
Assert-Equal $null (ConvertFrom-DurationText '20000') '越界 -> null'

Write-Host 'Test-IsSelfTarget'
$selfs = @('D:\app\startup-helper.ps1','D:\app\开机助手-双击运行.bat')
Assert-True  (Test-IsSelfTarget 'D:\app\startup-helper.ps1' $selfs)          '命中 .ps1'
Assert-True  (Test-IsSelfTarget 'D:\APP\STARTUP-HELPER.PS1' $selfs)          '大小写不敏感'
Assert-True  (Test-IsSelfTarget 'D:\app\开机助手-双击运行.bat' $selfs)        '命中 .bat'
Assert-True  (Test-IsSelfTarget 'D:/app/startup-helper.ps1' $selfs)          '正斜杠规范化'
Assert-True  (Test-IsSelfTarget 'D:\app\sub\..\startup-helper.ps1' $selfs)   '相对段规范化'
Assert-True  (-not (Test-IsSelfTarget 'D:\other\app.exe' $selfs))            '别的程序 -> false'
Assert-True  (-not (Test-IsSelfTarget '' $selfs))                            '空目标 -> false'
Assert-True  (-not (Test-IsSelfTarget 'C:\x.exe' @()))                       '空 self 列表 -> false'

Write-Host 'SP1 New-Reminder 周期字段默认'
$nr = New-Reminder
Assert-Equal 'daily' $nr.recurType   'recurType 默认 daily'
Assert-Equal 1  $nr.intervalDays     'intervalDays 默认 1'
Assert-Equal 1  $nr.monthlyDay       'monthlyDay 默认 1'
Assert-Equal '' $nr.anchorDate       'anchorDate 默认空'
Write-Host 'SP1 Read-Config 补周期字段'
$tmp = Join-Path $env:TEMP ("sp1_" + [guid]::NewGuid().ToString('N') + '.json')
'{"reminders":[{"time":"09:00","days":[],"message":"x","enabled":true}]}' | Set-Content -LiteralPath $tmp -Encoding UTF8
$rc = Read-Config $tmp; Remove-Item -LiteralPath $tmp -Force
Assert-Equal 'daily' $rc.reminders[0].recurType   '补 recurType=daily'
Assert-Equal 1  $rc.reminders[0].intervalDays      '补 intervalDays=1'
Assert-Equal 1  $rc.reminders[0].monthlyDay        '补 monthlyDay=1'
Assert-Equal '' $rc.reminders[0].anchorDate        '补 anchorDate 空'

Write-Host 'Test-RecurrenceDueToday'
$mon = [datetime]'2026-06-29'   # 周一(ISO1), 当天=29号; 6月30天
Assert-True  (Test-RecurrenceDueToday (New-Reminder @{recurType='daily';days=@()}) $mon)          'daily 空=每天'
Assert-True  (Test-RecurrenceDueToday (New-Reminder @{recurType='daily';days=@(1)}) $mon)         'daily 含周一'
Assert-True  (-not (Test-RecurrenceDueToday (New-Reminder @{recurType='daily';days=@(2)}) $mon))  'daily 不含周一'
$rn = New-Reminder @{recurType='everyNDays';intervalDays=2;anchorDate='2026-06-29'}
Assert-True  (Test-RecurrenceDueToday $rn $mon)                                                  'N天 anchor当天 true'
Assert-True  (-not (Test-RecurrenceDueToday $rn ([datetime]'2026-06-30')))       'N天 anchor+1 false'
Assert-True  (Test-RecurrenceDueToday $rn ([datetime]'2026-07-01'))              'N天 anchor+2 true'
Assert-True  (-not (Test-RecurrenceDueToday (New-Reminder @{recurType='everyNDays';intervalDays=2;anchorDate='2026-06-30'}) $mon)) 'N天 今天<anchor false'
Assert-True  (Test-RecurrenceDueToday (New-Reminder @{recurType='everyNDays';intervalDays=2;anchorDate=''}) $mon)                  'N天 空anchor=每天'
Assert-True  (Test-RecurrenceDueToday (New-Reminder @{recurType='monthly';monthlyDay=29}) $mon)                  'monthly 29号 true'
Assert-True  (-not (Test-RecurrenceDueToday (New-Reminder @{recurType='monthly';monthlyDay=28}) $mon))           'monthly 28号 false'
Assert-True  (Test-RecurrenceDueToday (New-Reminder @{recurType='monthly';monthlyDay=31}) ([datetime]'2026-06-30')) 'monthly 31夹到6月30'
Assert-True  (Test-RecurrenceDueToday (New-Reminder @{recurType='monthly';monthlyDay=31}) ([datetime]'2026-02-28')) 'monthly 31夹到2月28'

Write-Host 'Get-ReminderDecision 周期接入'
$rN3 = New-Reminder @{ time='09:00'; recurType='everyNDays'; intervalDays=2; anchorDate='2026-06-29' }
Assert-Equal 'none' (Get-ReminderDecision $rN3 ([datetime]'2026-06-30 09:00') ([datetime]'2026-06-30 08:00') (New-ReminderState)).action 'everyNDays 非周期日到点 -> none'
Assert-Equal 'arm'  (Get-ReminderDecision $rN3 ([datetime]'2026-07-01 09:00') ([datetime]'2026-07-01 08:00') (New-ReminderState)).action 'everyNDays 周期日到点 -> arm'

Write-Host 'SP2 popupTimeoutSeconds 字段'
Assert-Equal 0 (New-Reminder).popupTimeoutSeconds 'New-Reminder 默认 0'
$tmp = Join-Path $env:TEMP ("sp2_" + [guid]::NewGuid().ToString('N') + '.json')
'{"reminders":[{"time":"09:00","message":"x","enabled":true}]}' | Set-Content -LiteralPath $tmp -Encoding UTF8
$rc = Read-Config $tmp; Remove-Item -LiteralPath $tmp -Force
Assert-Equal 0 $rc.reminders[0].popupTimeoutSeconds '补 popupTimeoutSeconds=0'
Write-Host 'Get-PopupTimeoutSeconds'
Assert-Equal 30 (Get-PopupTimeoutSeconds (New-Reminder @{popupTimeoutSeconds=30})) '显式 30'
Assert-Equal 60 (Get-PopupTimeoutSeconds (New-Reminder @{popupTimeoutSeconds=0; repeatMinutes=5})) '0+重复 -> 60'
Assert-Equal 0  (Get-PopupTimeoutSeconds (New-Reminder @{popupTimeoutSeconds=0; repeatMinutes=0})) '0+不重复 -> 0'
Assert-Equal 0  (Get-PopupTimeoutSeconds (New-Reminder @{popupTimeoutSeconds=-5; repeatMinutes=0})) '负数视作 0 -> 0'
Assert-Equal 15 (Get-PopupTimeoutSeconds (New-Reminder @{popupTimeoutSeconds=15; repeatMinutes=5})) '显式优先于重复默认'

Write-Host 'ConvertFrom-CommandLine'
$c1 = ConvertFrom-CommandLine '"C:\a b\x.exe" --f 1'
Assert-Equal 'C:\a b\x.exe' $c1.Target    '引号路径 Target'
Assert-Equal '--f 1'        $c1.Arguments '引号路径 Args'
$c2 = ConvertFrom-CommandLine 'notepad.exe /a'
Assert-Equal 'notepad.exe'  $c2.Target    '无引号 Target'
Assert-Equal '/a'           $c2.Arguments '无引号 Args'
$c3 = ConvertFrom-CommandLine 'C:\x.exe'
Assert-Equal 'C:\x.exe'     $c3.Target    '纯路径 Target'
Assert-Equal ''             $c3.Arguments '纯路径无 Args'
$c4 = ConvertFrom-CommandLine '"C:\x.exe"'
Assert-Equal 'C:\x.exe'     $c4.Target    '仅引号路径 Target'
Assert-Equal ''             $c4.Arguments '仅引号路径无 Args'
$c5 = ConvertFrom-CommandLine ''
Assert-Equal ''             $c5.Target    '空 Target'
Assert-Equal ''             $c5.Arguments '空 Args'
$c6 = ConvertFrom-CommandLine '   '
Assert-Equal ''             $c6.Target    '空白 Target'

Write-Host 'New-ImportedLaunchStep'
$ri = [pscustomobject]@{ type='Registry'; name='Foo'; command='"C:\foo.exe" -x' }
$rs = New-ImportedLaunchStep $ri
Assert-Equal 'app'        $rs.kind    'Registry 导入 kind=app'
Assert-Equal 'Foo'        $rs.label   'label=name'
Assert-Equal 'C:\foo.exe' $rs.target  'Registry target 拆分'
Assert-Equal '-x'         $rs.args    'Registry args 拆分'
Assert-Equal 2000         $rs.delayMs '默认延迟 2000'
Assert-True  ([bool]$rs.enabled)      '默认启用'
$fi = [pscustomobject]@{ type='StartupFolder'; name='Bar.lnk'; command='C:\Bar.lnk' }
$fs = New-ImportedLaunchStep $fi
Assert-Equal 'C:\Bar.lnk'  $fs.target 'StartupFolder target=command'
Assert-Equal ''            $fs.args   'StartupFolder 无 args'

Write-Host 'SP5 字段默认 + Read-Config 补'
$n5 = New-Reminder
Assert-Equal 'any' $n5.startupHourMode 'startupHourMode 默认 any'
Assert-Equal 9     $n5.startupHour     'startupHour 默认 9'
$tmp = Join-Path $env:TEMP ("sp5_" + [guid]::NewGuid().ToString('N') + '.json')
'{"reminders":[{"time":"09:00","message":"x","enabled":true}]}' | Set-Content -LiteralPath $tmp -Encoding UTF8
$rc = Read-Config $tmp; Remove-Item -LiteralPath $tmp -Force
Assert-Equal 'any' $rc.reminders[0].startupHourMode '补 startupHourMode=any'
Assert-Equal 9     $rc.reminders[0].startupHour     '补 startupHour=9'
Write-Host 'Test-StartupHourOk'
Assert-True  (Test-StartupHourOk (New-Reminder @{startupHourMode='any'})              ([datetime]'2026-06-30 10:00')) 'any 不限 -> true'
Assert-True  (Test-StartupHourOk (New-Reminder @{startupHourMode='before';startupHour=9}) ([datetime]'2026-06-30 07:00')) 'before9 登录7点 -> true'
Assert-True  (-not (Test-StartupHourOk (New-Reminder @{startupHourMode='before';startupHour=9}) ([datetime]'2026-06-30 09:00'))) 'before9 登录9点 -> false(边界)'
Assert-True  (-not (Test-StartupHourOk (New-Reminder @{startupHourMode='before';startupHour=9}) ([datetime]'2026-06-30 10:00'))) 'before9 登录10点 -> false'
Assert-True  (Test-StartupHourOk (New-Reminder @{startupHourMode='after';startupHour=18}) ([datetime]'2026-06-30 19:00')) 'after18 登录19点 -> true'
Assert-True  (Test-StartupHourOk (New-Reminder @{startupHourMode='after';startupHour=18}) ([datetime]'2026-06-30 18:00')) 'after18 登录18点 -> true(边界)'
Assert-True  (-not (Test-StartupHourOk (New-Reminder @{startupHourMode='after';startupHour=18}) ([datetime]'2026-06-30 17:00'))) 'after18 登录17点 -> false'

Write-Host 'Get-ReminderDecision startup 时段限制'
$rb = New-Reminder @{ trigger='startup'; startupHourMode='before'; startupHour=9 }
Assert-Equal 'arm'  (Get-ReminderDecision $rb ([datetime]'2026-06-30 07:05') ([datetime]'2026-06-30 07:00') (New-ReminderState)).action 'before9 登录7点 -> arm'
Assert-Equal 'none' (Get-ReminderDecision $rb ([datetime]'2026-06-30 10:05') ([datetime]'2026-06-30 10:00') (New-ReminderState)).action 'before9 登录10点 -> none'
$ra = New-Reminder @{ trigger='startup'; startupHourMode='after'; startupHour=18 }
Assert-Equal 'arm'  (Get-ReminderDecision $ra ([datetime]'2026-06-30 19:05') ([datetime]'2026-06-30 19:00') (New-ReminderState)).action 'after18 登录19点 -> arm'
Assert-Equal 'none' (Get-ReminderDecision $ra ([datetime]'2026-06-30 17:05') ([datetime]'2026-06-30 17:00') (New-ReminderState)).action 'after18 登录17点 -> none'
$rn = New-Reminder @{ trigger='startup'; startupHourMode='any' }
Assert-Equal 'arm'  (Get-ReminderDecision $rn ([datetime]'2026-06-30 12:00') ([datetime]'2026-06-30 12:00') (New-ReminderState)).action 'any -> arm'

Write-Host 'SP6 稍后(snooze) 一次性重排'
Assert-True ($null -eq (New-ReminderState).snoozeUntil) 'New-ReminderState snoozeUntil 默认 null'
Write-Host 'Set-ReminderSnooze'
$ss = New-ReminderState; $ss.nextRepeatAt=[datetime]'2026-06-30 09:10'; $ss.repeatCount=3
$ss = Set-ReminderSnooze $ss ([datetime]'2026-06-30 09:00') 15
Assert-Equal ([datetime]'2026-06-30 09:15') $ss.snoozeUntil 'snooze 15 分钟 -> snoozeUntil=+15'
Assert-True ($null -eq $ss.nextRepeatAt) 'snooze 清掉进行中的周期重复'
Assert-Equal 3 $ss.repeatCount 'snooze 保留 repeatCount(不重置 MAX_REPEATS 安全帽)'
$ss2 = Set-ReminderSnooze (New-ReminderState) ([datetime]'2026-06-30 09:00') 0
Assert-Equal ([datetime]'2026-06-30 09:10') $ss2.snoozeUntil 'snooze <1 -> 默认 10 分钟'
Write-Host 'Get-ReminderDecision snooze'
$stD = New-ReminderState; $stD.snoozeUntil=[datetime]'2026-06-30 10:00'
$dD = Get-ReminderDecision (New-Reminder @{}) ([datetime]'2026-06-30 10:05') ([datetime]'2026-06-30 08:00') $stD
Assert-Equal 'fire' $dD.action 'snooze 到点 -> fire'
Assert-True ($null -eq $stD.snoozeUntil) 'snooze fire 后清 snoozeUntil'
$stN = New-ReminderState; $stN.snoozeUntil=[datetime]'2026-06-30 10:00'
Assert-Equal 'none' (Get-ReminderDecision (New-Reminder @{}) ([datetime]'2026-06-30 09:55') ([datetime]'2026-06-30 08:00') $stN).action 'snooze 未到点 -> none'
# 回归: 跨午夜落到非周期日(everyNDays anchor+1)的 snooze 仍要 fire, 不被周期门清掉
$rRec = New-Reminder @{ recurType='everyNDays'; intervalDays=2; anchorDate='2026-06-30' }
$stR = New-ReminderState; $stR.snoozeUntil=[datetime]'2026-07-01 00:20'
Assert-Equal 'fire' (Get-ReminderDecision $rRec ([datetime]'2026-07-01 00:25') ([datetime]'2026-06-30 23:00') $stR).action 'snooze 在非周期日仍 fire(不被周期门丢弃)'

Write-Host 'SP7 提醒稳定 id（计时器运行时状态键，避免改文案/同名同时刻串状态）'
Assert-True (-not [string]::IsNullOrWhiteSpace([string](New-Reminder).id)) 'New-Reminder 自带非空 id'
Assert-True ((New-Reminder).id -ne (New-Reminder).id) '两次 New-Reminder id 不同'
$tmp7 = Join-Path $env:TEMP ("sp7_" + [guid]::NewGuid().ToString('N') + '.json')
'{"reminders":[{"time":"09:00","message":"x","enabled":true}]}' | Set-Content -LiteralPath $tmp7 -Encoding UTF8
$rc7 = Read-Config $tmp7; Remove-Item -LiteralPath $tmp7 -Force
Assert-True (-not [string]::IsNullOrWhiteSpace([string]$rc7.reminders[0].id)) 'Read-Config 给缺 id 的提醒补非空 id'

Write-Host 'AG1 message 步骤字段 + New-ActionGroup 默认'
$ms = New-LaunchStep 'message'
Assert-Equal ''     $ms.message      'message 步骤 message 默认空'
Assert-Equal $false ([bool]$ms.speak) 'message 步骤 speak 默认 false'
Assert-Equal $false ([bool]$ms.confirm) 'message 步骤 confirm 默认 false'
Assert-Equal 'none' $ms.onYes.type   'message 步骤 onYes.type 默认 none'
$g = New-ActionGroup @{ name='心流' }
Assert-True  (-not [string]::IsNullOrWhiteSpace([string]$g.id)) 'New-ActionGroup 自带非空 id'
Assert-Equal '心流' $g.name          'name 取 Props'
Assert-True  ([bool]$g.enabled)      'enabled 默认 true'
Assert-Equal 0 @($g.steps).Count     'steps 默认空'
Assert-True  (-not ($g.PSObject.Properties.Name -contains 'atStartup'))    'New-ActionGroup 不再含 atStartup'
Assert-True  (-not ($g.PSObject.Properties.Name -contains 'timedEnabled')) 'New-ActionGroup 不再含 timedEnabled'
Assert-True  ((New-ActionGroup).id -ne (New-ActionGroup).id) '两次 id 不同'

Write-Host 'AG2 配置集成'
Assert-Equal 0 @((Get-DefaultConfig).actionGroups).Count 'Get-DefaultConfig actionGroups 默认空数组'
$tmpA = Join-Path ([System.IO.Path]::GetTempPath()) ("ag2_{0}.json" -f [guid]::NewGuid().ToString('N'))
'{"reminders":[],"launchSteps":[],"actionGroups":[{"name":"心流","atStartup":true}]}' | Set-Content -LiteralPath $tmpA -Encoding UTF8
$rcA = Read-Config $tmpA; Remove-Item -LiteralPath $tmpA -Force
Assert-True  (-not [string]::IsNullOrWhiteSpace([string]$rcA.actionGroups[0].id)) '补组 id 非空'
Assert-Equal '心流' $rcA.actionGroups[0].name '保留 name'
$tmpB = Join-Path ([System.IO.Path]::GetTempPath()) ("ag2b_{0}.json" -f [guid]::NewGuid().ToString('N'))
'{"reminders":[],"launchSteps":[]}' | Set-Content -LiteralPath $tmpB -Encoding UTF8
$rcB = Read-Config $tmpB; Remove-Item -LiteralPath $tmpB -Force
Assert-Equal 0 @($rcB.actionGroups).Count '缺 actionGroups -> 补空数组'

Write-Host 'AG3 Resolve-ActionGroup'
$rgA = New-ActionGroup @{ name='甲' }; $rgB = New-ActionGroup @{ name='乙' }
Assert-Equal '乙' (Resolve-ActionGroup @($rgA,$rgB) $rgB.id).name 'id 命中返回对应组'
Assert-True  ($null -eq (Resolve-ActionGroup @($rgA,$rgB) 'no-such-id')) '未命中返回 null'
Assert-True  ($null -eq (Resolve-ActionGroup @($rgA,$rgB) '')) '空 id 返回 null'
Assert-True  ($null -eq (Resolve-ActionGroup @() $rgA.id)) '空列表返回 null'

Write-Host 'LG group 步骤'
$gs = New-LaunchStep 'group' @{ groupId='abc'; label='心流组' }
Assert-Equal 'group' $gs.kind        'kind=group'
Assert-Equal 'abc'   $gs.groupId     'groupId 取 Props'
Assert-Equal ''      (New-LaunchStep 'group').groupId 'groupId 默认空'
Assert-Equal '动作组' (Get-StepKindLabel 'group')      'group 类型文案'
Assert-Equal '运行动作组：心流组' (Get-StepSummary $gs) 'group 摘要用 label'
Assert-Equal '运行动作组：abc'   (Get-StepSummary (New-LaunchStep 'group' @{ groupId='abc' })) 'group 摘要无 label 用 id'

Write-Host 'RM silentGroupId'
Assert-Equal '' (New-Reminder).silentGroupId 'New-Reminder silentGroupId 默认空'
$tmpS = Join-Path ([System.IO.Path]::GetTempPath()) ("rms_{0}.json" -f [guid]::NewGuid().ToString('N'))
'{"launchSteps":[],"actionGroups":[],"reminders":[{"time":"09:00"}]}' | Set-Content -LiteralPath $tmpS -Encoding UTF8
$rcS = Read-Config $tmpS; Remove-Item -LiteralPath $tmpS -Force
Assert-Equal '' ([string]$rcS.reminders[0].silentGroupId) 'Read-Config 补 silentGroupId=空'

Write-Host 'startup 提醒的开机时段门控（startupWithinMinutes）'
$tS = [datetime]'2026-06-30 09:00'
$rU = New-Reminder @{ trigger='startup'; message='hi' }   # 默认 startupWithinMinutes=10
Assert-Equal 'arm'  (Get-ReminderDecision $rU $tS $tS (New-ReminderState) -UptimeMinutes 5).action  '开机5分钟内启动 -> 算登录，arm'
$stU2 = New-ReminderState
Assert-Equal 'none' (Get-ReminderDecision $rU $tS $tS $stU2 -UptimeMinutes 30).action '开机30分钟后启动 -> 不算登录，none'
Assert-Equal 'none' (Get-ReminderDecision $rU $tS $tS $stU2 -UptimeMinutes 5).action  '门控后同次运行不再判定（startupHandled）'
$rU0 = New-Reminder @{ trigger='startup'; message='hi'; startupWithinMinutes=0 }
Assert-Equal 'arm'  (Get-ReminderDecision $rU0 $tS $tS (New-ReminderState) -UptimeMinutes 300).action '0=不限，每次启动都算'
Assert-Equal 'arm'  (Get-ReminderDecision $rU $tS $tS (New-ReminderState)).action '未传 UptimeMinutes（-1）不门控，保持旧行为'
$tmpU = Join-Path ([System.IO.Path]::GetTempPath()) ("coreu_{0}.json" -f [guid]::NewGuid().ToString('N'))
'{"launchSteps":[],"actionGroups":[],"reminders":[{"trigger":"startup","message":"x"}]}' | Set-Content -LiteralPath $tmpU -Encoding UTF8
$rcU = Read-Config $tmpU; Remove-Item -LiteralPath $tmpU -Force
Assert-Equal 0 ([int]$rcU.reminders[0].startupWithinMinutes) '旧配置补 0（不限）——不静默改变老用户「每次启动都弹」的行为'
Assert-Equal 10 ([int](New-Reminder).startupWithinMinutes) '新建提醒默认 10 分钟门控'
Assert-True ((Get-SystemUptimeMinutes) -ge 0) 'Get-SystemUptimeMinutes 非负'

Write-Host 'CR 修复回归（code review 确认项）'
# 手改 json 的坏时间不再抛异常（原来每 tick 弹崩溃框并挡住后续提醒）
$rBad = New-Reminder @{ time='9:00' }
Assert-Equal 'none' (Get-ReminderDecision $rBad ([datetime]'2026-06-30 09:01') ([datetime]'2026-06-30 08:00') (New-ReminderState)).action '非法 time 解析失败 -> none 不抛'
# SendKeys 命名键：裸词会被当逐字符序列（^Enter = Ctrl+E + 打字 nter）
Assert-Equal '^{ENTER}' (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Ctrl+Enter')) 'Ctrl+Enter -> ^{ENTER}'
Assert-Equal '%{TAB}'   (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Alt+Tab'))    'Alt+Tab -> %{TAB}'
Assert-Equal '{ESC}'    (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Esc'))        'Esc -> {ESC}'
Assert-Equal '^a'       (ConvertTo-SendKeysString (ConvertFrom-KeyCombo 'Ctrl+A'))     '单字母仍走小写路径'
# 缺 days 字段的手写步骤：摘要不再出现假星期后缀
$bareStep = [pscustomobject]@{ kind='system'; command='showDesktop'; onlyBefore8=$false }
Assert-True ((Get-StepSummary $bareStep) -notmatch '（') '缺 days 摘要无假后缀'

Write-Host 'Add-ItemAfter（新增插到选中项之后）'
$base = @('a','b','c')
$r1 = Add-ItemAfter $base 'X' 0
Assert-Equal 'a,X,b,c' ($r1.Items -join ',') '选中第0项 -> 插到其后'
Assert-Equal 1 $r1.NewIndex '新项落点=选中+1'
$r2 = Add-ItemAfter $base 'X' 2
Assert-Equal 'a,b,c,X' ($r2.Items -join ',') '选中末项 -> 追加末尾（区间不反向）'
Assert-Equal 3 $r2.NewIndex '末项后落点=Count'
$r3 = Add-ItemAfter $base 'X' -1
Assert-Equal 'a,b,c,X' ($r3.Items -join ',') '无选中(-1) -> 追加末尾'
Assert-Equal 3 $r3.NewIndex '无选中落点=Count'
$r4 = Add-ItemAfter $base 'X' 9
Assert-Equal 'a,b,c,X' ($r4.Items -join ',') '越界索引 -> 追加末尾'
$r5 = Add-ItemAfter @() 'X' -1
Assert-Equal 'X' ($r5.Items -join ',') '空数组 -> 单元素'
Assert-Equal 0 $r5.NewIndex '空数组落点=0'
$r6 = Add-ItemAfter $null 'X' 0
Assert-Equal 'X' ($r6.Items -join ',') '$null 数组 -> 单元素'

Write-Host 'CN1 窗口步骤新增等待字段进入默认 schema（缺省 0 = 保持现有秒关语义）'
$ws = New-LaunchStep 'window'
Assert-True ($ws.PSObject.Properties.Name -contains 'waitForWindowSeconds') '默认 schema 含 waitForWindowSeconds'
Assert-True ($ws.PSObject.Properties.Name -contains 'postWindowDelaySeconds') '默认 schema 含 postWindowDelaySeconds'
Assert-Equal 0 ([int]$ws.waitForWindowSeconds) 'waitForWindowSeconds 默认 0'
Assert-Equal 0 ([int]$ws.postWindowDelaySeconds) 'postWindowDelaySeconds 默认 0'

Write-Host 'CN2 两字段可由 Props 设定'
$ws2 = New-LaunchStep 'window' @{ waitForWindowSeconds=120; postWindowDelaySeconds=5 }
Assert-Equal 120 ([int]$ws2.waitForWindowSeconds) 'waitForWindowSeconds 可设'
Assert-Equal 5 ([int]$ws2.postWindowDelaySeconds) 'postWindowDelaySeconds 可设'

Write-Host 'Toast 时长：自动关闭>=20 秒 -> 长通知，否则短'
Assert-Equal $true  (Test-ReminderToastLong (New-Reminder @{ popupTimeoutSeconds=60 })) '60 秒 -> 长通知'
Assert-Equal $true  (Test-ReminderToastLong (New-Reminder @{ popupTimeoutSeconds=20 })) '20 秒(边界) -> 长通知'
Assert-Equal $false (Test-ReminderToastLong (New-Reminder @{ popupTimeoutSeconds=19 })) '19 秒 -> 短通知'
Assert-Equal $false (Test-ReminderToastLong (New-Reminder @{ popupTimeoutSeconds=0 }))  '0 秒(默认) -> 短通知'

Write-Host 'Bug 回归：跨 runspace 传动作组的 JSON 往返（PS5.1 ConvertFrom-Json 数组陷阱 → 点是运行组无反应）'
$bugG1 = New-ActionGroup @{ name='甲'; steps=@((New-LaunchStep 'system' @{ command='showDesktop' })) }
$bugG2 = New-ActionGroup @{ name='乙' }
# 复刻 Invoke-ReminderAsync 的序列化（WpfGui.ps1 line 535），再用安全反序列化还原
$bugJson = ConvertTo-Json @(@($bugG1, $bugG2)) -Depth 8
$bugRt = ConvertFrom-JsonArray $bugJson
Assert-Equal 2 (@($bugRt).Count) '两个组往返后仍是 2 个（不被套成 1）'
Assert-True ($null -ne (Resolve-ActionGroup $bugRt $bugG1.id)) '往返后能按 id 解析到组（点是即可运行）'
# 单组（ConvertTo-Json 会把单元素数组降成对象）仍要能还原并解析
$bugRt1 = ConvertFrom-JsonArray (ConvertTo-Json @(@($bugG1)) -Depth 8)
Assert-Equal 1 (@($bugRt1).Count) '单个组往返后为 1'
Assert-True ($null -ne (Resolve-ActionGroup $bugRt1 $bugG1.id)) '单组往返可解析'
Assert-Equal 0 (@(ConvertFrom-JsonArray '[]')).Count '空 JSON 数组 -> 0'
Assert-Equal 0 (@(ConvertFrom-JsonArray '')).Count '空串 -> 0'

Write-Host 'Bug 回归：时间归一 Format-TimeHHmm（单数小时 9:00 会让 ParseExact/repeatUntil 正则判定失败）'
Assert-Equal '09:00' (Format-TimeHHmm '9:00')  '单数小时补零 9:00 -> 09:00'
Assert-Equal '09:00' (Format-TimeHHmm ' 9:00 ') '首尾空格 + 单数小时'
Assert-Equal '09:00' (Format-TimeHHmm '09:00') '规范值保持不变'
Assert-Equal '23:00' (Format-TimeHHmm '23:00') '23:00 保持'
Assert-Equal ''      (Format-TimeHHmm '')       '空串 -> 空（repeatUntil 无截止）'
Assert-Equal 'abc'   (Format-TimeHHmm 'abc')    '非法原样返回（不误伤，交下游按原逻辑）'
Assert-Equal '25:00' (Format-TimeHHmm '25:00')  '越界小时原样返回'
# 归一后主时间能正常 arm（修复前 time=9:00 会 ParseExact 抛异常 -> 永不触发）
$fixT = New-Reminder @{ time=(Format-TimeHHmm '9:00'); days=@() }
$mon0900 = Get-Date '2026-06-29 09:00:30'
Assert-Equal 'arm' (Get-ReminderDecision $fixT $mon0900 $mon0900 (New-ReminderState)).action '归一后 9:00 提醒能触发'
$rawT = New-Reminder @{ time='9:00'; days=@() }
Assert-Equal 'none' (Get-ReminderDecision $rawT $mon0900 $mon0900 (New-ReminderState)).action '未归一的 9:00 确实不触发（bug 现场）'

Invoke-TestSummary
