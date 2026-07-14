$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '_assert.ps1')
. (Join-Path $here '..\lib\Clockwork.Core.ps1')
. (Join-Path $here '..\lib\Clockwork.Win32.ps1')
. (Join-Path $here '..\lib\Clockwork.Actions.ps1')
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

Write-Host 'AX13b 工作目录留空 -> 默认目标所在目录（完整路径）；裸程序名不设'
$sysDir = Join-Path $env:WINDIR 'system32'
$script:spCalls.Clear()
Invoke-LaunchItem (New-LaunchStep 'app' @{ label='wd'; target=(Join-Path $sysDir 'notepad.exe'); workDir='' }) | Out-Null
Assert-True ([string]$script:spCalls[0] -match 'WorkingDirectory') '留空 + 完整路径目标 -> 设了 WorkingDirectory'
Assert-True ([string]$script:spCalls[0] -match [regex]::Escape($sysDir)) 'WorkingDirectory 取目标所在目录'
$script:spCalls.Clear()
Invoke-LaunchItem (New-LaunchStep 'app' @{ label='wd2'; target='notepad.exe'; workDir='' }) | Out-Null
Assert-True (-not ([string]$script:spCalls[0] -match 'WorkingDirectory')) '裸程序名(非完整路径) -> 不设 WorkingDirectory'
$script:spCalls.Clear()
Invoke-LaunchItem (New-LaunchStep 'app' @{ label='wd3'; target=(Join-Path $sysDir 'notepad.exe'); workDir='C:\' }) | Out-Null
Assert-True ([string]$script:spCalls[0] -match 'WorkingDirectory:\s*C:\\') '显式工作目录仍原样使用（不被目标目录覆盖）'

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

Write-Host 'AX15b 静默动作组提醒返回 Action=空（配了重复催促才能续期，#4）'
$rS = New-Reminder @{ time='09:00'; silentGroupId='no-such'; repeatMinutes=30 }   # 组找不到即返回，不执行任何动作，仍验证返回值
$pS = Invoke-Reminder $rS @() 3>$null
Assert-Equal '' ([string]$pS.Action) "静默组返回 Action='' 而非 'ok'（否则 Update-ReminderAfterFire 会清掉重复计划、只跑一次）"

Write-Host 'AX15c ConvertTo-KeysVk 键名→VK（发键与热键注册共用，#10）'
Assert-Equal 13  (ConvertTo-KeysVk 'Enter') 'Enter -> 13'
Assert-Equal 27  (ConvertTo-KeysVk 'esc')   '别名 esc -> Escape 27'
Assert-Equal 53  (ConvertTo-KeysVk '5')     '单数字 5 -> D5 53'
Assert-Equal 65  (ConvertTo-KeysVk 'A')     '字母 A -> 65'
Assert-Equal 116 (ConvertTo-KeysVk 'F5')    'F5 -> 116'
Assert-Equal 0   (ConvertTo-KeysVk '10')    '多位数字拒绝 -> 0'
Assert-Equal 0   (ConvertTo-KeysVk 'Bogus') '无法识别 -> 0'
Assert-Equal 0   (ConvertTo-KeysVk '')      '空 -> 0'
Assert-Equal ([int](ConvertTo-KeysVk '5')) ([int](ConvertTo-HotkeyParams 'Ctrl+5').Vk) '共用后：热键注册与发键解析对同一键名一致（不再漂移）'

Write-Host 'AX16 动作组命名互斥锁：同组已在运行则跳过（跨 runspace 也有效）'
# 注意：Mutex 对同一线程可重入，必须在【另一个线程】持锁才能模拟「别处正在跑该组」
$grpM = New-ActionGroup @{ name='GM'; steps=@( (New-LaunchStep 'system' @{ command='showDesktop' }) ) }
$rsM = [runspacefactory]::CreateRunspace(); $rsM.Open()
$psM = [powershell]::Create(); $psM.Runspace = $rsM
[void]$psM.AddScript('param($n) $m = New-Object System.Threading.Mutex($true, $n); Start-Sleep -Seconds 2; $m.ReleaseMutex(); $m.Dispose()').AddArgument('Local\rockbenben.clockwork.group.' + [string]$grpM.id)
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

Write-Host 'AX19 发送文本步骤 -> Send-Text 透传（文本 + 目标进程）'
$script:sentText = $null; $script:sentProc = $null
function Send-Text { param([string]$Text,[string]$Process='') $script:sentText = $Text; $script:sentProc = $Process; 'unverified' }
Invoke-StepAction (New-LaunchStep 'text' @{ text='hello world' }) | Out-Null
Assert-Equal 'hello world' ([string]$script:sentText) 'text 步骤把文本透传给 Send-Text'
Assert-Equal '' ([string]$script:sentProc) '未填目标进程 -> Process 为空'
Invoke-StepAction (New-LaunchStep 'text' @{ text='hi'; process='notepad' }) | Out-Null
Assert-Equal 'notepad' ([string]$script:sentProc) '填了目标进程 -> 透传给 Send-Text 的 Process'

Write-Host 'AX19b 已运行则激活：有窗口只激活不启动 / 无窗口照常启动 / 未勾选照常启动'
$script:fgCalls = New-Object System.Collections.ArrayList
$script:winCount = 1   # 受测切换：>0 表示有窗口
function Get-AppWindowHandles { param([string]$Process) if ($script:winCount -gt 0) { ,@(1) } else { ,@() } }
function Set-ForegroundAppWindow { param([string]$Process) [void]$script:fgCalls.Add($Process); $true }
$script:spCalls.Clear()
# 勾选 + 有窗口 -> 只激活、不 Start-Process
$script:winCount = 1
Invoke-LaunchItem (New-LaunchStep 'app' @{ target='C:\x\msedge.exe'; activateIfRunning=$true }) | Out-Null
Assert-Equal 'msedge' ([string]$script:fgCalls[0]) '有窗口 -> 置前进程 msedge'
Assert-Equal 0 $script:spCalls.Count '有窗口 -> 不启动'
# 勾选 + 无窗口 -> 照常启动
$script:fgCalls.Clear(); $script:spCalls.Clear(); $script:winCount = 0
Invoke-LaunchItem (New-LaunchStep 'app' @{ target='C:\x\msedge.exe'; activateIfRunning=$true }) | Out-Null
Assert-Equal 0 $script:fgCalls.Count '无窗口 -> 不置前'
Assert-True ($script:spCalls.Count -ge 1) '无窗口 -> 照常启动'
# 未勾选 -> 照常启动（不查窗口）
$script:fgCalls.Clear(); $script:spCalls.Clear(); $script:winCount = 1
Invoke-LaunchItem (New-LaunchStep 'app' @{ target='C:\x\msedge.exe'; activateIfRunning=$false }) | Out-Null
Assert-Equal 0 $script:fgCalls.Count '未勾选 -> 不置前'
Assert-True ($script:spCalls.Count -ge 1) '未勾选 -> 照常启动'
# 手填进程名覆盖
$script:fgCalls.Clear(); $script:spCalls.Clear(); $script:winCount = 1
Invoke-LaunchItem (New-LaunchStep 'app' @{ target='C:\x\launcher.exe'; activateIfRunning=$true; activateProcess='RealApp' }) | Out-Null
Assert-Equal 'RealApp' ([string]$script:fgCalls[0]) '手填进程名覆盖自动推导'

Write-Host 'AX20 窗口风格 -> Start-Process -WindowStyle；备用路径 -> 主路径不存在用备用'
$realExe = Join-Path $env:WINDIR 'notepad.exe'
$script:spCalls.Clear()
Invoke-LaunchItem (New-LaunchStep 'app' @{ target=$realExe; windowStyle='minimized' }) | Out-Null
Assert-True ([string]$script:spCalls[0] -match 'WindowStyle:?\s*Minimized') '窗口风格 minimized -> -WindowStyle Minimized'
$script:spCalls.Clear()
Invoke-LaunchItem (New-LaunchStep 'app' @{ target=$realExe; windowStyle='' }) | Out-Null
Assert-True (-not ([string]$script:spCalls[0] -match 'WindowStyle')) '正常/留空 -> 不设 WindowStyle'
$script:spCalls.Clear()
Invoke-LaunchItem (New-LaunchStep 'app' @{ target='Z:\no\such.exe'; altTargets=("Z:\nope`n" + $realExe) }) | Out-Null
Assert-True ([string]$script:spCalls[0] -match [regex]::Escape($realExe)) '主路径不存在 -> 启动第一个存在的备用路径'

Write-Host 'AX21 循环动作：顶层步骤 repeat=3 -> 执行 3 次、日志标注第 i/3 次'
$logR = Join-Path ([System.IO.Path]::GetTempPath()) ("ax21_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfgRep = [pscustomobject]@{ actionGroups=@(); launchSteps=@( (New-LaunchStep 'system' @{ command='__nope__'; label='X'; repeat=3 }) ) }
$rRep = Invoke-LaunchSequence $cfgRep $logR
$cRep = Get-Content -LiteralPath $logR -Raw -Encoding UTF8; Remove-Item -LiteralPath $logR -Force
Assert-Equal 3 ([int]$rRep.Total) 'repeat=3 计 3 次执行'
Assert-Equal 3 ([int]$rRep.Fail)  '每次都如实计 Fail'
Assert-True ($cRep -match '第 1/3 次') '日志标注第 1/3 次'
Assert-True ($cRep -match '第 3/3 次') '日志标注第 3/3 次'

Write-Host 'AX22 循环动作：group 步骤 repeat=2 -> 整组展开两遍；组内步骤 repeat 亦生效'
$grpRep = New-ActionGroup @{ name='GR'; steps=@( (New-LaunchStep 'system' @{ command='__nope__'; repeat=2 }) ) }
$logG2 = Join-Path ([System.IO.Path]::GetTempPath()) ("ax22_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfgG2 = [pscustomobject]@{ actionGroups=@($grpRep); launchSteps=@( (New-LaunchStep 'group' @{ groupId=$grpRep.id; label='GR'; repeat=2 }) ) }
$rG2 = Invoke-LaunchSequence $cfgG2 $logG2
$cG2 = Get-Content -LiteralPath $logG2 -Raw -Encoding UTF8; Remove-Item -LiteralPath $logG2 -Force
Assert-Equal 4 ([int]$rG2.Total) '组×2 × 组内步骤×2 = 4 次执行'
Assert-Equal 4 ([int]$rG2.Fail)  '4 次都计 Fail'
Assert-True ($cG2 -match '运行动作组：GR（第 1/2 次）') '组头标注第 1/2 次'
Assert-True ($cG2 -match '运行动作组：GR（第 2/2 次）') '组头标注第 2/2 次'

Write-Host 'AX23 Get-StepRunMarkRepeat：Fail 累计、Mark 取第一个非 ✓（单步「运行」归纳）'
$rrRep = Get-StepRunMarkRepeat (New-LaunchStep 'system' @{ command='__nope2__'; repeat=2 })
Assert-Equal 2 ([int]$rrRep.Fail) 'repeat=2 两次都计 Fail'
Assert-True ([string]$rrRep.Mark -match '未知系统命令') 'Mark 保留告警文本'
$rrOld = Get-StepRunMarkRepeat ([pscustomobject]@{ enabled=$true; kind='system'; command='__nope3__' })   # 旧配置无 repeat
Assert-Equal 1 ([int]$rrOld.Fail) '缺 repeat 字段 -> 跑一次'

Write-Host 'AX24 循环动作：Invoke-ActionGroup 内步骤 repeat 生效；旧步骤(无 repeat)跑一次'
$script:axCount = 0
function Invoke-SystemCommand { param([string]$Command) if ($Command -eq '__count__') { $script:axCount++ } }   # 计数桩（本测试起遮蔽真函数）
$grpCnt = New-ActionGroup @{ name='GC2'; steps=@( (New-LaunchStep 'system' @{ command='__count__'; repeat=3 }) ) }
Invoke-ActionGroup $grpCnt
Assert-Equal 3 $script:axCount '组内 repeat=3 执行 3 次'
$script:axCount = 0
$grpCnt2 = New-ActionGroup @{ name='GC3'; steps=@( ([pscustomobject]@{ enabled=$true; kind='system'; command='__count__'; delayMs=0 }) ) }
Invoke-ActionGroup $grpCnt2
Assert-Equal 1 $script:axCount '缺 repeat 字段 -> 跑一次'

Write-Host 'AX25 急停：步骤循环中置停止信号 -> 弃跑剩余次数与后续步骤'
Clear-StopAll
function Invoke-SystemCommand { param([string]$Command) switch ($Command) { '__count__' { $script:axCount++ } '__stop__' { $script:axCount++; Request-StopAll } } }   # 计数桩 + 急停桩
$script:axCount = 0
$logS = Join-Path ([System.IO.Path]::GetTempPath()) ("ax25_{0}.log" -f [guid]::NewGuid().ToString('N'))
$cfgS = [pscustomobject]@{ actionGroups=@(); launchSteps=@(
    (New-LaunchStep 'system' @{ command='__stop__'; label='S'; repeat=3 }),
    (New-LaunchStep 'system' @{ command='__count__'; label='C' }) ) }
$rS = Invoke-LaunchSequence $cfgS $logS
$cS = Get-Content -LiteralPath $logS -Raw -Encoding UTF8; Remove-Item -LiteralPath $logS -Force
Assert-Equal 1 $script:axCount '第 1 次执行后即停：不跑第 2/3 次、不跑下一步'
Assert-Equal 1 ([int]$rS.Total) 'Total 只计已执行的 1 次'
Assert-True ([bool]$rS.Stopped) '返回 Stopped=true'
Assert-True ($cS -match '已手动停止') '日志记「已手动停止」'
Clear-StopAll

Write-Host 'AX26 急停：动作组内置停止信号 -> 中止整组剩余步骤'
$script:axCount = 0
$grpS = New-ActionGroup @{ name='GS'; steps=@(
    (New-LaunchStep 'system' @{ command='__stop__' }),
    (New-LaunchStep 'system' @{ command='__count__' }) ) }
Invoke-ActionGroup $grpS
Assert-Equal 1 $script:axCount '组内第 1 步置停止后第 2 步不再执行'
Clear-StopAll

Write-Host 'AX27 急停：Get-StepRunMarkRepeat 循环间响应'
$script:axCount = 0
$rrS = Get-StepRunMarkRepeat (New-LaunchStep 'system' @{ command='__stop__'; repeat=5 })
Assert-Equal 1 $script:axCount 'repeat=5 只跑 1 次即停'
Clear-StopAll

Write-Host 'AX27b 评审4-#1: Invoke-InRunspaceAsync 建立阶段抛错 -> 仍以 $null 调 OnDone（不泄漏在跑标志、不卡死急停清理）'
# 注入让「计时器创建」同步抛错，模拟建 runspace/BeginInvoke 阶段失败：此时 OnDone 本不会被计时器调度，
# 若不兜底，调用方的 Running/ReminderFiring 标志永远清不掉 → Test-AnyRunActive 恒真 → 急停信号永久卡死。
$axDone = @{ count=0; out='sentinel' }
$script:MakeAsyncTimer = { throw '注入：建立失败' }
Invoke-InRunspaceAsync -Script { 1 } -OnDone { param($o) $axDone.count++; $axDone.out=$o } 3>$null
$script:MakeAsyncTimer = $null
Assert-Equal 1 ([int]$axDone.count) '建立失败 -> OnDone 仍被调用一次（兜底清标志）'
Assert-True ($null -eq $axDone.out) 'OnDone 收到 $null（各 OnDone 都先清标志再用输出、容忍 $null）'

Write-Host 'AX28 ConvertTo-HotkeyParams（急停键解析）'
$hp1 = ConvertTo-HotkeyParams 'Ctrl+Alt+F12'
Assert-True ($null -ne $hp1) 'Ctrl+Alt+F12 可解析'
Assert-Equal 3 ([int]$hp1.Modifiers) 'Ctrl(2)+Alt(1)=3'
Assert-Equal 123 ([int]$hp1.Vk) 'F12 -> VK 123'
$hp2 = ConvertTo-HotkeyParams 'Win+Shift+Q'
Assert-Equal 12 ([int]$hp2.Modifiers) 'Win(8)+Shift(4)=12'
Assert-Equal ([int][System.Windows.Forms.Keys]::Q) ([int]$hp2.Vk) 'Q 的 VK'
$hp3 = ConvertTo-HotkeyParams 'Ctrl+5'
Assert-Equal ([int][System.Windows.Forms.Keys]::D5) ([int]$hp3.Vk) '数字 5 -> D5'
Assert-True ($null -eq (ConvertTo-HotkeyParams 'Ctrl+')) '缺主键 -> null'
Assert-True ($null -eq (ConvertTo-HotkeyParams 'Ctrl+10')) '多位数字 -> null'
Assert-True ($null -eq (ConvertTo-HotkeyParams '')) '空 -> null'

Write-Host 'AX29 fix#1: Reset-StopIfIdle 仅在全空闲时复位急停信号（有运行在跑则保留，不吞并发急停）'
Clear-StopAll
$script:LaunchState = @{ Running = $true }; $script:StepActionState = @{ Running = $false }; $script:ActionGroupRunning = @{}
Request-StopAll
Reset-StopIfIdle
Assert-True (Test-StopRequested) '有启动序列在跑 -> 保留急停信号（不被并发操作擦掉）'
$script:LaunchState = @{ Running = $false }; $script:ActionGroupRunning = @{ 'g1' = $true }
Reset-StopIfIdle
Assert-True (Test-StopRequested) '有动作组在跑 -> 保留急停信号'
$script:ActionGroupRunning = @{ 'g1' = $false }
Assert-True (-not (Test-AnyRunActive)) '全 false -> 无运行在跑'
Reset-StopIfIdle
Assert-True (-not (Test-StopRequested)) '全空闲 -> 复位急停信号'

Write-Host 'AX30 fix#4: 单步运行 group 步骤按其 repeat 跑整组 N 遍（Invoke-ActionGroupAsync -Repeat）'
Clear-StopAll
$script:axCount = 0
function Invoke-SystemCommand { param([string]$Command) if ($Command -eq '__gr__') { $script:axCount++ } }   # 计数桩
$grpR = New-ActionGroup @{ name='GRR'; steps=@( (New-LaunchStep 'system' @{ command='__gr__' }) ) }
Invoke-ActionGroupAsync $grpR 3 0
Assert-Equal 3 $script:axCount 'group 步骤 repeat=3 -> 整组跑 3 遍'
$script:axCount = 0
Invoke-ActionGroupAsync $grpR
Assert-Equal 1 $script:axCount '默认 repeat=1 -> 一遍（托盘/提醒触发不变）'
Clear-StopAll

Write-Host 'AX30b fix#1(评审回归): 背景/同步路径（AppRoot 未设=提醒 runspace）绝不复位急停信号'
# $script:AppRoot 在测试里为 $null（顶部设定），Invoke-ActionGroupAsync 走同步兜底=模拟提醒背景 runspace。
# 修复前：入口无条件 Reset-StopIfIdle -> 在此擦掉别处按下的急停（评审确认的 bug）。修复后：同步路径不复位。
Clear-StopAll
$script:axCount = 0
function Invoke-SystemCommand { param([string]$Command) if ($Command -eq '__gr2__') { $script:axCount++ } }
$grpBg = New-ActionGroup @{ name='GBG'; steps=@( (New-LaunchStep 'system' @{ command='__gr2__' }) ) }
Request-StopAll   # 模拟：主线程正为在跑的启动序列按下了急停
Invoke-ActionGroupAsync $grpBg   # 提醒背景路径触发同一组
Assert-True (Test-StopRequested) '背景/同步路径未擦掉急停信号（急停得以保留）'
Assert-Equal 0 $script:axCount '急停在效 -> 该组本身也不执行（fail-safe）'
Clear-StopAll

Write-Host 'AX30c 评审3-#2: Test-AnyRunActive 也识别提醒在跑（$script:ReminderFiring），Reset-StopIfIdle 不误清其急停'
$script:LaunchState=@{Running=$false}; $script:StepActionState=@{Running=$false}; $script:ActionGroupRunning=@{}; $script:ReminderFiring=@{}
Assert-True (-not (Test-AnyRunActive)) '全空 -> 无运行在跑'
$script:ReminderFiring=@{ 'r1'=$true }
Assert-True (Test-AnyRunActive) '有提醒在弹/在跑 -> 视为有运行（背景 runspace 不设主线程守卫，靠此表识别）'
Clear-StopAll; Request-StopAll
Reset-StopIfIdle
Assert-True (Test-StopRequested) '提醒在跑时 Reset-StopIfIdle 保留急停信号（不擦掉为提醒组按下的急停）'
$script:ReminderFiring=@{}
Reset-StopIfIdle
Assert-True (-not (Test-StopRequested)) '提醒结束、全空闲 -> 复位'
Clear-StopAll

Write-Host 'AX31 fix#4b: Invoke-ActionGroupRepeat 急停在遍间中止'
Clear-StopAll
$script:axCount = 0
function Invoke-SystemCommand { param([string]$Command) if ($Command -eq '__grs__') { $script:axCount++; Request-StopAll } }   # 跑一遍即置停
$grpRS = New-ActionGroup @{ name='GRS'; steps=@( (New-LaunchStep 'system' @{ command='__grs__' }) ) }
Invoke-ActionGroupRepeat $grpRS 5 0
Assert-Equal 1 $script:axCount 'repeat=5 但第 1 遍置停 -> 只跑 1 遍'
Clear-StopAll

Write-Host 'AX32 fix#5: 急停在效时窗口步骤返回 0 不误报「找不到窗口」'
function Invoke-WindowAction { param([string]$Process,[string]$Op,[string]$SendKey='{ENTER}',[int]$WaitForWindowSeconds=0,[int]$PostWindowDelaySeconds=0) 0 }   # 桩：恒返回 0（模拟急停打断/无窗口）
$stW = New-LaunchStep 'window' @{ process='whatever'; action='minimize' }
Request-StopAll
$rrW = Get-StepRunMark $stW
Assert-Equal '✓' ([string]$rrW.Mark) '急停在效 -> 静默不告警（记 ✓）'
Assert-Equal 0 ([int]$rrW.Fail) '急停在效 -> 不计 Fail'
Clear-StopAll
$rrW2 = Get-StepRunMark $stW
Assert-Equal 1 ([int]$rrW2.Fail) '无急停 -> 返回 0 仍如实告警（原行为不变）'

Write-Host 'AX18 窗口 sendkey 成功记「~ 已发送（未校验）」而非 ✓（#2）'
function Invoke-WindowAction { param($Process,$Op,$SendKey,$Wait,$Post) $true }   # 模拟置前+发送成功（放最后，不影响上面的真实窗口测试）
$mkSK = Get-StepRunMark ([pscustomobject]@{ kind='window'; action='sendkey'; process='x'; sendKey='{ENTER}'; waitForWindowSeconds=0; postWindowDelaySeconds=0 })
Assert-Equal 1 ([int]$mkSK.Unver) 'sendkey 成功计入「已发送未校验」'
Assert-Equal 0 ([int]$mkSK.Fail)  'sendkey 成功不算失败'
Assert-True ($mkSK.Mark -match '未校验') 'Mark 标为 ~ 已发送（未校验）'

Write-Host 'AX19 Invoke-ActionGroup 不把步骤成功流漏进返回值（#2）'
function Invoke-StepAction { param($Step) 'unverified' }   # mock：模拟步骤的 'unverified' 成功流输出（放最后，不影响上面真实测试）
$grpLeak = New-ActionGroup @{ name='leak'; enabled=$true; steps=@((New-LaunchStep 'keys' @{ combo='Enter' })) }
$leakOut = @(Invoke-ActionGroup $grpLeak)
Assert-Equal 0 $leakOut.Count "动作组执行不漏出步骤的 'unverified' 等成功流对象"

Invoke-TestSummary
