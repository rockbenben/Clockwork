$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '_assert.ps1')
. (Join-Path $here '..\lib\StartupHelper.Core.ps1')
. (Join-Path $here '..\lib\StartupHelper.Win32.ps1')
. (Join-Path $here '..\lib\StartupHelper.Actions.ps1')
$script:AppRoot = $null   # 强制 Invoke-ActionGroupAsync 走同步兜底，避免测试起后台 runspace
Initialize-Win32Types

Write-Host 'AX1 group 启动步骤：找不到组'
$log1 = Join-Path ([System.IO.Path]::GetTempPath()) ("ax1_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfg1 = [pscustomobject]@{ actionGroups=@(); launchSteps=@( (New-LaunchStep 'group' @{ groupId='nope'; label='X' }) ) }
$r1 = Invoke-LaunchSequence $cfg1 $log1
Assert-Equal 1 ([int]$r1.Fail) '找不到组计一次 fail'
$c1 = Get-Content -LiteralPath $log1 -Raw -Encoding UTF8; Remove-Item -LiteralPath $log1 -Force
Assert-True ($c1 -match '找不到动作组') '日志记「找不到动作组」'

Write-Host 'AX2 group 启动步骤：展开组内步骤'
$grp = New-ActionGroup @{ name='G'; steps=@( (New-LaunchStep 'window' @{ process='nope_xyz_proc'; action='minimize' }) ) }
$log2 = Join-Path ([System.IO.Path]::GetTempPath()) ("ax2_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfg2 = [pscustomobject]@{ actionGroups=@($grp); launchSteps=@( (New-LaunchStep 'group' @{ groupId=$grp.id; label='G' }) ) }
$r2 = Invoke-LaunchSequence $cfg2 $log2
$c2 = Get-Content -LiteralPath $log2 -Raw -Encoding UTF8; Remove-Item -LiteralPath $log2 -Force
Assert-True ($c2 -match '运行动作组：G') '写组头行'
Assert-True ($c2 -match '没有找到') '展开写组内 window 步骤告警'
Assert-Equal 1 ([int]$r2.Fail) '组内窗口找不到计入 fail'

Write-Host 'AX3 提醒 onYes=group 未找到组 -> 告警不抛'
$w3 = @(Invoke-ReminderAction @{ type='group'; target='no-such' } @() 3>&1)
Assert-True ((@($w3 | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })).Count -ge 1) 'group 未找到记 Warning'

Write-Host 'AX4 group 展开后 Total 计执行的动作数（非顶层步数）'
$grpT = New-ActionGroup @{ name='GT'; steps=@(
    (New-LaunchStep 'window' @{ process='nope_a_proc'; action='minimize' }),
    (New-LaunchStep 'window' @{ process='nope_b_proc'; action='minimize' })
) }
$logT = Join-Path ([System.IO.Path]::GetTempPath()) ("ax4_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfgT = [pscustomobject]@{ actionGroups=@($grpT); launchSteps=@( (New-LaunchStep 'group' @{ groupId=$grpT.id; label='GT' }) ) }
$rT = Invoke-LaunchSequence $cfgT $logT; Remove-Item -LiteralPath $logT -Force
Assert-Equal 2 ([int]$rT.Total) 'Total=展开的 2 个子步骤(非顶层 1)'
Assert-Equal 2 ([int]$rT.Fail)  '两个子步骤都失败计入 Fail'

Write-Host 'AX5 禁用的动作组被引用时跳过、不运行、不计步'
$grpD = New-ActionGroup @{ name='GD'; enabled=$false; steps=@(
    (New-LaunchStep 'window' @{ process='nope_c_proc'; action='minimize' })
) }
$logD = Join-Path ([System.IO.Path]::GetTempPath()) ("ax5_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfgD = [pscustomobject]@{ actionGroups=@($grpD); launchSteps=@( (New-LaunchStep 'group' @{ groupId=$grpD.id; label='GD' }) ) }
$rD = Invoke-LaunchSequence $cfgD $logD
$cD = Get-Content -LiteralPath $logD -Raw -Encoding UTF8; Remove-Item -LiteralPath $logD -Force
Assert-Equal 0 ([int]$rD.Total) '禁用组不计入步数'
Assert-Equal 0 ([int]$rD.Fail)  '禁用组跳过不计失败'
Assert-True ($cD -match '已禁用，跳过') '日志写明已禁用跳过'

Write-Host 'AX6 close 幂等语义：目标没在运行 = 目标已达成，不告警计 ✓'
$stC = New-LaunchStep 'window' @{ process='nope_close_proc'; action='close'; label='关闭 不存在' }
$rc = Get-StepRunMark $stC
Assert-Equal 0 ([int]$rc.Fail) 'close 不存在的进程不计 fail'
Assert-Equal '✓' ([string]$rc.Mark) 'close 不存在的进程记 ✓'
$stM = New-LaunchStep 'window' @{ process='nope_close_proc'; action='minimize' }
$rm = Get-StepRunMark $stM
Assert-Equal 1 ([int]$rm.Fail) 'minimize 不存在的进程仍如实告警'

Write-Host 'AX7 窗口动作早退：无窗口时 close/minimize 返回 0（不做无谓激活等待）'
Assert-Equal 0 ([int](Invoke-WindowAction 'nope_zzz_proc' 'close')) 'close 无窗口返回 0'
Assert-Equal 0 ([int](Invoke-WindowAction 'nope_zzz_proc' 'minimize')) 'minimize 无窗口返回 0'

Write-Host 'AX8 组内步骤的时间条件同样生效（不满足即跳过，不执行不计数）'
$todayIso = [int](Get-Date).DayOfWeek; if ($todayIso -eq 0) { $todayIso = 7 }
$otherDay = if ($todayIso -eq 1) { 2 } else { 1 }
$grpC = New-ActionGroup @{ name='GC'; steps=@(
    (New-LaunchStep 'window' @{ process='nope_cond_proc'; action='minimize'; days=@($otherDay) })   # 仅别的星期 → 今天跳过
) }
$logC = Join-Path ([System.IO.Path]::GetTempPath()) ("ax8_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfgC = [pscustomobject]@{ actionGroups=@($grpC); launchSteps=@( (New-LaunchStep 'group' @{ groupId=$grpC.id; label='GC' }) ) }
$rC = Invoke-LaunchSequence $cfgC $logC; Remove-Item -LiteralPath $logC -Force
Assert-Equal 0 ([int]$rC.Total) '条件不满足的组内步骤不执行不计数'
Assert-Equal 0 ([int]$rC.Fail)  '也不产生失败'

Write-Host 'AX9 Test-StepCondition 兼容缺失字段（手写 json 无 days/onlyBefore8 = 无限制）'
$bare = [pscustomobject]@{ enabled=$true; kind='system'; command='showDesktop' }
Assert-True (Test-StepCondition $bare 12) '缺 days/onlyBefore8 视为无限制'

Write-Host 'AX10 只读系统启动项不可接管（策略/Winlogon 等 type=Registry 但 canToggle=false）'
$ro = [pscustomobject]@{ type='Registry'; name='Shell'; command='explorer.exe'; canToggle=$false; regHive=''; regRunKind=''; valueName='Shell' }
$cfgRO = [pscustomobject]@{ launchSteps=@(); actionGroups=@() }
Assert-True ((Import-StartupItemToChecklist $ro $cfgRO) -match '只读') '只读项接管被明确拒绝'
Assert-Equal 0 (@($cfgRO.launchSteps).Count) '未追加任何步骤'

Write-Host 'AX11 带空格路径的 .ps1 启动步骤能真正执行（-File 路径加引号）'
$spDir = Join-Path ([System.IO.Path]::GetTempPath()) ("sp ace_{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $spDir | Out-Null
$flag = Join-Path $spDir 'flag.txt'
Set-Content -LiteralPath (Join-Path $spDir 'run me.ps1') -Value "Set-Content -LiteralPath '$flag' -Value ok" -Encoding UTF8
Invoke-LaunchItem (New-LaunchStep 'app' @{ label='sp'; target=(Join-Path $spDir 'run me.ps1') }) | Out-Null
$deadline=(Get-Date).AddSeconds(15); while(-not (Test-Path -LiteralPath $flag) -and (Get-Date) -lt $deadline){ Start-Sleep -Milliseconds 300 }
Assert-True (Test-Path -LiteralPath $flag) '带空格路径的 .ps1 实际跑起来了'
Remove-Item -LiteralPath $spDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'AX11b Wait-SystemReady：已就绪则 0 等待返回（探针注入，不依赖真机/真时钟）'
$rReady = Wait-SystemReady -PollMs 100 -ShellProbe { $true } -NetProbe { $true } -Sleeper { param($ms) }
Assert-True ([bool]$rReady.Ready) '两探针即真 -> Ready'
Assert-Equal 0 ([int]$rReady.WaitedMs) '就绪时零等待'

Write-Host 'AX11c Wait-SystemReady：就绪即走——第 3 次轮询才就绪，等待 2*Poll'
$script:probeN = 0
$rDelay = Wait-SystemReady -PollMs 100 -TimeoutSeconds 90 `
    -ShellProbe { $script:probeN++; $script:probeN -ge 3 } -NetProbe { $true } -Sleeper { param($ms) }
Assert-True ([bool]$rDelay.Ready) '最终就绪'
Assert-Equal 200 ([int]$rDelay.WaitedMs) '第3次轮询就绪 = 睡了 2 次 Poll'

Write-Host 'AX11d Wait-SystemReady：始终不就绪则封顶放行（Ready=false，等待=封顶）'
$rCap = Wait-SystemReady -PollMs 100 -TimeoutSeconds 1 -ShellProbe { $false } -NetProbe { $true } -Sleeper { param($ms) }
Assert-True (-not [bool]$rCap.Ready) '超时未就绪 -> Ready=false'
Assert-Equal 1000 ([int]$rCap.WaitedMs) '等待到封顶（1s）即放行'

Write-Host 'AX11e Wait-SystemReady：RequireNetwork=false 时不看网络探针'
$rNoNet = Wait-SystemReady -PollMs 100 -RequireNetwork $false -ShellProbe { $true } -NetProbe { throw '不该被调用' } -Sleeper { param($ms) }
Assert-True ([bool]$rNoNet.Ready) '不要求网络时 Shell 就绪即放行'

Write-Host 'AX11f Wait-SystemReady：探针抛异常按「就绪」放行，绝不因探针故障卡死'
$rErr = Wait-SystemReady -PollMs 100 -ShellProbe { throw 'boom' } -NetProbe { $true } -Sleeper { param($ms) }
Assert-True ([bool]$rErr.Ready) '探针异常 -> 视为就绪放行'
Assert-Equal 0 ([int]$rErr.WaitedMs) '异常即放行、零等待'

Write-Host 'AX12 设为音量 = 先取消静音再设音量（静音下只改百分比等于没调）'
$script:volCalls = New-Object System.Collections.ArrayList
function Set-SystemMute   { param([bool]$Mute) [void]$script:volCalls.Add("mute:$Mute") }
function Set-SystemVolume { param([int]$Percent) [void]$script:volCalls.Add("vol:$Percent") }
Invoke-StepAction (New-LaunchStep 'volume' @{ action='set'; level=40 })
Assert-Equal 'mute:False' ([string]$script:volCalls[0]) 'set 先取消静音'
Assert-Equal 'vol:40'     ([string]$script:volCalls[1]) '再设音量'

Write-Host 'AX13 旧配置 onYes type=sound 兼容（已并入 运行/打开文件，行为不变）'
$script:spCalls = New-Object System.Collections.ArrayList
function Start-Process { [void]$script:spCalls.Add(($args -join ' ')) }   # 函数遮蔽 cmdlet，记录调用
Invoke-ReminderAction ([pscustomobject]@{ type='sound'; target='D:\music\a.mp3' }) @()
Assert-Equal 1 $script:spCalls.Count 'sound 仍触发一次打开'
Assert-True ([string]$script:spCalls[0] -match 'a\.mp3') 'sound 目标按文件关联打开'

Write-Host 'AX14 无动作提醒走系统日常通知（不再弹置顶自绘窗）'
$script:toastCalls = New-Object System.Collections.ArrayList
function Show-SystemToast { param([string]$Message,[string]$Title='') [void]$script:toastCalls.Add($Message); $true }
function Show-ReminderPopup { throw '不该走到自绘弹窗' }
$rT = New-Reminder @{ time='09:00'; message='喝水' }   # 无 onYes
$pT = Invoke-Reminder $rT @()
Assert-Equal 1 $script:toastCalls.Count '无动作提醒发了一条系统通知'
Assert-Equal '' ([string]$pT.Action) '返回未确认（重复催促型照常继续催）'

Write-Host 'AX15 重复催促型提醒不走 Toast（Toast 无「确定/稍后」，会催不停）'
$script:toastCalls2 = New-Object System.Collections.ArrayList
function Show-SystemToast { param([string]$Message,[string]$Title='') [void]$script:toastCalls2.Add($Message); $true }
function Show-ReminderPopup { param([string]$Message,[bool]$Confirm,[int]$AutoDismissSeconds=0,[switch]$NoSnooze) [pscustomobject]@{ Action='ok'; SnoozeMinutes=$null } }
$rN = New-Reminder @{ time='09:00'; message='催我'; repeatMinutes=5 }
$pN = Invoke-Reminder $rN @()
Assert-Equal 0 $script:toastCalls2.Count '重复型未发 Toast'
Assert-Equal 'ok' ([string]$pN.Action) '走了可交互弹窗（确定可停催）'

Write-Host 'AX16 动作组命名互斥锁：同组已在运行则跳过（跨 runspace 也有效）'
# 注意：Mutex 对同一线程可重入，必须在【另一个线程】持锁才能模拟「别处正在跑该组」
$grpM = New-ActionGroup @{ name='GM'; steps=@( (New-LaunchStep 'system' @{ command='showDesktop' }) ) }
$rsM = [runspacefactory]::CreateRunspace(); $rsM.Open()
$psM = [powershell]::Create(); $psM.Runspace = $rsM
[void]$psM.AddScript('param($n) $m = New-Object System.Threading.Mutex($true, $n); Start-Sleep -Seconds 2; $m.ReleaseMutex(); $m.Dispose()').AddArgument('Local\rockbenben.startupHelper.group.' + [string]$grpM.id)
$hM = $psM.BeginInvoke()
Start-Sleep -Milliseconds 400   # 等对方线程真正拿到锁
$wM = @(Invoke-ActionGroup $grpM 3>&1)
Assert-True ((@($wM | Where-Object { $_ -is [System.Management.Automation.WarningRecord] -and $_.Message -match '已在运行' })).Count -eq 1) '他处持锁时本次触发被拒并告警'
$null = $psM.EndInvoke($hM); $psM.Dispose(); $rsM.Dispose()
# 锁释放后可正常运行（showDesktop 无告警）
$wM2 = @(Invoke-ActionGroup $grpM 3>&1)
Assert-Equal 0 (@($wM2 | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })).Count '锁释放后正常运行'

Write-Host 'AX17a Wait-AppWindow：窗口已在 -> 零等待即走（探针注入，不依赖真窗口/真时钟）'
$rw = Wait-AppWindow -TimeoutSeconds 120 -PollMs 100 -Probe { $true } -Sleeper { param($ms) }
Assert-True ([bool]$rw.Present) '窗口在 -> Present'
Assert-Equal 0 ([int]$rw.WaitedMs) '窗口在 -> 零等待'

Write-Host 'AX17b Wait-AppWindow：超时 0 -> 只探一次即返回（保持现有早退语义，绝不睡）'
$rw0 = Wait-AppWindow -TimeoutSeconds 0 -PollMs 100 -Probe { $false } -Sleeper { param($ms) throw '超时0不该睡' }
Assert-True (-not [bool]$rw0.Present) '超时0且无窗口 -> Present=false'
Assert-Equal 0 ([int]$rw0.WaitedMs) '超时0 -> 一次探测、零等待'

Write-Host 'AX17c Wait-AppWindow：第 3 次轮询窗口才出现 -> 出现即走，等 2*Poll'
$script:winN = 0
$rw3 = Wait-AppWindow -TimeoutSeconds 90 -PollMs 100 -Probe { $script:winN++; $script:winN -ge 3 } -Sleeper { param($ms) }
Assert-True ([bool]$rw3.Present) '最终出现 -> Present'
Assert-Equal 200 ([int]$rw3.WaitedMs) '第3次探到 = 睡了 2 次 Poll'

Write-Host 'AX17d Wait-AppWindow：始终无窗口 -> 封顶返回 Present=false（宁可放弃也不挂死）'
$rwc = Wait-AppWindow -TimeoutSeconds 1 -PollMs 100 -Probe { $false } -Sleeper { param($ms) }
Assert-True (-not [bool]$rwc.Present) '超时未出现 -> Present=false'
Assert-Equal 1000 ([int]$rwc.WaitedMs) '等到封顶（1s）'

Write-Host 'AX18 Invoke-StepAction 把窗口步骤的等待字段透传给 Invoke-WindowAction'
$script:waArgs = $null
function Invoke-WindowAction { param([string]$Process,[string]$Op,[string]$SendKey='{ENTER}',[int]$WaitForWindowSeconds=0,[int]$PostWindowDelaySeconds=0) $script:waArgs=@{P=$Process;Op=$Op;W=$WaitForWindowSeconds;D=$PostWindowDelaySeconds}; 1 }
Invoke-StepAction (New-LaunchStep 'window' @{ process='Weixin'; action='close'; waitForWindowSeconds=120; postWindowDelaySeconds=5 })
Assert-Equal 'Weixin' ([string]$script:waArgs.P) '透传进程名'
Assert-Equal 120 ([int]$script:waArgs.W) '透传 waitForWindowSeconds'
Assert-Equal 5 ([int]$script:waArgs.D) '透传 postWindowDelaySeconds'

Invoke-TestSummary
