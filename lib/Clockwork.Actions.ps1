# Clockwork.Actions.ps1 —— 编排：依赖 Core + Win32

# 接管：先停用原项(可逆 StartupApproved)，成功才把它作为 app 步骤加入清单。返回 'Ok'/'NeedsAdmin'/'Error:...'。
function Import-StartupItemToChecklist {
    param($Item, $Config)
    # 防御纵深：只接管 Run 键/启动文件夹项（即便 GUI 已门控，函数自身也自保）。
    if ([string]$Item.type -notin 'Registry','StartupFolder') { return 'Error: 仅支持 Run 键/启动文件夹项' }
    # 只读项（策略 Run / RunOnce / Winlogon / Active Setup）也是 type=Registry 但 canToggle=false、regHive 为空：
    # 不拦会拿空 hive 去拼注册表路径抛怪错，且不该动这些系统关键项。
    if ($Item.PSObject.Properties['canToggle'] -and -not [bool]$Item.canToggle) { return 'Error: 该项为只读（策略/系统项），不支持接管' }
    $res = Set-SystemStartupItemEnabled $Item $false
    if ($res -ne 'Ok') { return $res }
    $step = New-ImportedLaunchStep $Item
    # 去重：清单里已有相同 target 的步骤就不再追加，避免重复纳入导致双重启动。
    $dup = @($Config.launchSteps) | Where-Object { [string]$step.target -ne '' -and [string]$_.target -eq [string]$step.target } | Select-Object -First 1
    if (-not $dup) { $Config.launchSteps = @($Config.launchSteps) + $step }
    elseif (-not $dup.enabled) { $dup.enabled = $true }   # 已有同目标步骤但被禁用：启用它——否则刚关掉系统自启、清单那条又没启用，该程序两边都不启动（界面却报成功）
    'Ok'
}

function Invoke-LaunchItem {
    param($Item)
    # 备用路径：主路径(完整路径)不存在时，用「备用路径」里第一个存在的候选（每行一条），解决多设备路径不一致。
    $tgt = Resolve-LaunchTarget $Item.target $Item.altTargets
    if ($script:LaunchSelfPaths -and (Test-IsSelfTarget $tgt $script:LaunchSelfPaths)) {
        Write-Warning "跳过「$($Item.label)」：目标是Clockwork自身，避免开机自启动循环"
        return
    }
    # 已运行则激活窗口、不重复启动：进程名手填优先，否则从目标推导；有窗口就置前并跳过启动。
    # 原生不可用（受限令牌下编译失败）时给一句清楚提示，然后照常启动（不因此中断）。
    if ($Item.activateIfRunning) {
        if (Confirm-Win32Available) {
            $pn = if ($Item.activateProcess) { [string]$Item.activateProcess } else { Get-TargetProcessName $tgt }
            $pn = $pn -replace '\.exe$', ''
            if ($pn -and (Get-AppWindowHandles $pn).Count -gt 0) { [void](Set-ForegroundAppWindow $pn); return }
        } else { Write-Warning (Get-NativeRestrictedMsg) }
    }
    try {
        if ([string]$tgt -match '\.ps1$') {
            # .ps1 直接用 PowerShell 跑（否则 Start-Process 走文件关联「打开」→ 进编辑器而非执行）。
            # -File 路径必须手动加引号：PS5.1 的 ArgumentList 用空格拼接且不加引号，带空格的路径会被拆散。
            $psArgs = [System.Collections.Generic.List[string]]@('-NoProfile','-ExecutionPolicy','Bypass','-File', ('"' + [string]$tgt + '"'))
            if ($Item.args) { $psArgs.Add([string]$Item.args) }
            $sp = @{ FilePath = 'powershell.exe'; ArgumentList = $psArgs.ToArray(); PassThru = $true }
        } else {
            $sp = @{ FilePath = $tgt; PassThru = $true }
            if ($Item.args) { $sp.ArgumentList = $Item.args }
        }
        # 工作目录留空时，默认用「目标」所在目录（仅当目标是完整路径且该目录存在）——多数程序期望 cwd=自身目录。
        # 目标为裸程序名(notepad.exe)/网址/文档关联时不套用，交给系统默认。
        if ($Item.workDir) { $sp.WorkingDirectory = $Item.workDir }
        elseif ($tgt) {
            $td = try { if ([System.IO.Path]::IsPathRooted([string]$tgt)) { Split-Path -Parent ([string]$tgt) } else { '' } } catch { '' }
            if ($td -and (Test-Path -LiteralPath $td -PathType Container)) { $sp.WorkingDirectory = $td }
        }
        # 窗口风格：最小化/最大化/隐藏启动（正常/留空=不设，交给程序默认）。是否生效取决于目标程序。
        $ws = switch ([string]$Item.windowStyle) { 'minimized' {'Minimized'} 'maximized' {'Maximized'} 'hidden' {'Hidden'} default {''} }
        if ($ws) { $sp.WindowStyle = $ws }
        if ($Item.elevated) { $sp.Verb = 'RunAs' }
        $proc = Start-Process @sp
        # Start-Process 不抛错只代表「进程被拉起」，不代表程序真的跑起来。
        # 秒退且退出码非 0 = 多半崩溃/启动失败；退出码 0 的自退（启动器拉起子进程后退出）不误报。
        # 拿不到进程对象（ShellExecute 打开文档/URL）则跳过，保持 ✓。
        if ($proc) {
            try {
                if ($proc.WaitForExit(500) -and $proc.ExitCode -ne 0) {
                    Write-Warning "「$($Item.label)」启动后 0.5 秒内退出（退出码 $($proc.ExitCode)），疑似启动失败"
                }
            } catch {}
        }
    } catch {
        Write-Warning "启动失败：$($Item.label) —— $($_.Exception.Message)"
    }
}

# 破坏性系统命令(注销/重启/关机)执行前弹确认。无 UI 上下文(理论不出现)时保守跳过、不执行。
function Confirm-Destructive {
    param([string]$Action)
    try {
        # 用 WinForms MessageBox（非 WPF）：本函数在启动序列 / 单步「运行」的后台 runspace 里被调用，
        # 那两处只 Add-Type System.Windows.Forms、没有 PresentationFramework —— 原来的 [System.Windows.MessageBox]
        # (WPF) 在那里类型解析失败 → 抛异常 → 走 catch 返回 $false → 关机/重启/注销步骤被【静默跳过】。
        # 三个 runspace（含动作组）与主进程都载入了 WinForms，故改用它，各路径统一可弹确认。
        $r = [System.Windows.Forms.MessageBox]::Show("确定要执行「$Action」吗？", 'Clockwork · 确认',
            [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Warning)
        return ($r -eq [System.Windows.Forms.DialogResult]::Yes)
    } catch { Write-Warning "无法弹出确认框，已跳过「$Action」"; return $false }
}

function Invoke-SystemCommand {
    param([string]$Command)
    switch ($Command) {
        'showDesktop'     {
            # 原生 Shell COM（等价 Win+D，但不注入按键：不受焦点/UIPI 影响，结果可信 ✓ 而非「已发送未校验」）；失败才退回模拟按键。
            try { (New-Object -ComObject Shell.Application).ToggleDesktop() } catch { Send-KeyCombo 'Win+D' }
        }
        'lockScreen'      { Start-Process rundll32.exe 'user32.dll,LockWorkStation' }
        'taskManager'     { Start-Process taskmgr.exe }
        'clearClipboard'  { try { [System.Windows.Forms.Clipboard]::Clear() } catch { try { Set-Clipboard -Value ' ' } catch { Write-Warning "清空剪贴板失败：$($_.Exception.Message)" } } }
        'monitorOff'      { try { [void][SH.Native]::PostMessage([IntPtr]0xFFFF, 0x0112, [IntPtr]0xF170, [IntPtr]2) } catch { Write-Warning "关闭显示器失败：$($_.Exception.Message)" } }
        'hibernate'       { Start-Process shutdown.exe '/h' }
        'signOut'         { if (Confirm-Destructive '注销') { Start-Process shutdown.exe '/l' } }
        'restart'         { if (Confirm-Destructive '重启') { Start-Process shutdown.exe '/r /t 0' } }
        'shutdown'        { if (Confirm-Destructive '关机') { Start-Process shutdown.exe '/s /t 0' } }
        'emptyRecycleBin' {
            # 「回收站已空」不算失败、也不该每次跑都误告警（原来靠报错文字含「空/empty」判断，依赖系统语言，德语等误报）。
            # 定案（勿再改回「先清、出错再数」）：先数条目——空就静默跳过（本就无事可做），非空才清、清失败才如实告警。
            #   · 不依赖语言；· 已知非空才清，真失败必告警、不会被吞；· Shell 回收站枚举是标准可靠 API，「数了非空却清不动」才告警合理。
            try {
                $bin = (New-Object -ComObject Shell.Application).NameSpace(0xA)
                if (@($bin.Items()).Count -gt 0) { Clear-RecycleBin -Force -ErrorAction Stop }
            } catch { Write-Warning "清空回收站失败：$($_.Exception.Message)" }
        }
        'openSettings'    { Start-Process 'ms-settings:' }
        'screenshot'      {
            # 原生截图协议（Win10 1809+ / Win11 的截图覆盖层），不注入按键；协议缺失才退回 Win+Shift+S。
            try { Start-Process 'ms-screenclip:' -ErrorAction Stop } catch { Send-KeyCombo 'Win+Shift+S' }
        }
        'sleep'           {
            # 经典坑：rundll32 无法传类型化参数，powrprof,SetSuspendState 的实参全被忽略——在开启休眠的机器上
            # 会直接「休眠」而非睡眠。用 .NET 原生 SetSuspendState 明确指定 Suspend；失败才退回旧方式。
            try { [void][System.Windows.Forms.Application]::SetSuspendState([System.Windows.Forms.PowerState]::Suspend, $false, $false) }
            catch { Start-Process rundll32.exe 'powrprof.dll,SetSuspendState 0,1,0' }
        }
        default           { Write-Warning "未知系统命令：$Command" }
    }
}

function Invoke-StepAction {
    param($Step)
    switch ($Step.kind) {
        'app'  { Invoke-LaunchItem $Step }
        'keys' { Send-KeyCombo $Step.combo }
        'volume' {
            switch ($Step.action) {
                'mute'   { Set-SystemMute $true }
                'unmute' { Set-SystemMute $false }
                # 设为音量 = 想听到声音：系统若处于静音，只改音量百分比等于没调 → 先取消静音再设
                'set'    { Set-SystemMute $false; Set-SystemVolume ([int]$Step.level) }
                default  { Write-Warning "未知音量操作：$($Step.action)" }
            }
        }
        'window' {
            # Invoke-WindowAction 返回「操作了几个窗口」；0 = 目标进程没在跑/没窗口。
            # close 例外：目标态就是「不在运行」，本就没开 = 目标已达成，按幂等语义记 ✓ 不告警
            # （动作组常批量关一串常驻应用，逐个 ⚠ 会把「有警告」淹没成噪音）。其余动作 0 仍如实告警。
            $n = Invoke-WindowAction $Step.process ([string]$Step.action) $Step.sendKey ([int]$Step.waitForWindowSeconds) ([int]$Step.postWindowDelaySeconds)
            # 急停打断等窗口/出现后延迟时 Invoke-WindowAction 也返回 0——那是「用户停了」不是「没找到窗口」，
            # 不能误报 ⚠（否则用户停后读日志会去排查一个不存在的窗口检测问题）。停止在效则静默，由序列/组的停止分支收尾。
            # sendkey：键注入前台窗口后无法证实对方是否接收（同 发送按键/文本）→ 成功输出 'unverified' 标「~ 已发送（未校验）」，
            # 不能记 ✓（否则用户以为自动登录成功了）。$n=$false（窗口没出现 或 抢不到前台）才告警；急停打断也返 false，Test-StopRequested 时静默。
            if ([string]$Step.action -eq 'sendkey') {
                if ($n) { 'unverified' }
                elseif (-not (Test-StopRequested)) { Write-Warning "未能向「$($Step.process)」发送按键：窗口没出现，或找到了却无法把它带到最前（后台/开机自启触发时常见）" }
            }
            # 其余动作（close/minimize/maximize/activate）：$n 是操作的窗口数，0=没找到窗口。close 幂等不告警；急停返回 0 也静默。
            elseif ([int]$n -le 0 -and [string]$Step.action -ne 'close' -and -not (Test-StopRequested)) {
                Write-Warning "没有找到「$($Step.process)」的窗口（进程未运行或尚未就绪？），$($Step.action) 未生效"
            }
        }
        'system' { Invoke-SystemCommand ([string]$Step.command) }
        'text'   { Send-Text ([string]$Step.text) ([string]$Step.process) }
        # 纯延时步骤：本身无动作，等待由步骤末尾统一的 delayMs（Start-Sleep）完成——复用现成的分步延时机制，不另起 Sleep。
        'delay'  { }
        default  { Write-Warning "未知步骤类型：$($Step.kind)" }
    }
}

# 跑单个步骤并归纳标记：捕获告警流(真失败)与输出流里的 'unverified'(发射后不管)。
# 返回 @{ Mark; Fail; Unver }，Mark 不含前导空格。启动序列顶层步骤与组内展开步骤共用。
function Get-StepRunMark {
    param($Step)
    $note = ''; $unverified = $false; $f = 0; $u = 0
    try {
        $out   = @(Invoke-StepAction $Step 3>&1)
        $warns = @($out | Where-Object { $_ -is [System.Management.Automation.WarningRecord] })
        if ($warns.Count) { $note = (($warns | ForEach-Object { $_.Message }) -join '；'); $f = 1 }
        elseif ($out | Where-Object { $_ -is [string] -and $_ -eq 'unverified' }) { $unverified = $true; $u = 1 }
    } catch { $note = "异常：$($_.Exception.Message)"; $f = 1 }
    $mark = if ($note) { "⚠ $note" } elseif ($unverified) { '~ 已发送（未校验）' } else { '✓' }
    @{ Mark = $mark; Fail = $f; Unver = $u }
}

# 跑单个步骤 repeat 次并归纳标记（单步「运行」用；循环动作的测试路径）：每次之间等 delayMs（末次不等——
# 手动运行后面没有下一步）；Mark 取第一个非 ✓ 的标记（如实暴露失败），Fail/Unver 累计。
function Get-StepRunMarkRepeat {
    param($Step)
    $rep = Get-StepRepeat $Step
    $mark = '✓'; $fail = 0; $unver = 0
    for ($i = 1; $i -le $rep; $i++) {
        $rr = Get-StepRunMark $Step
        $fail += $rr.Fail; $unver += $rr.Unver
        if ($mark -eq '✓' -and [string]$rr.Mark -ne '✓') { $mark = [string]$rr.Mark }
        if ($i -lt $rep) {   # 急停：循环之间响应（可中断延时/信号检查），收到即弃跑剩余次数
            if (Test-StopRequested) { break }
            if ([int]$Step.delayMs -gt 0 -and -not (Start-InterruptibleSleep ([int]$Step.delayMs))) { break }
        }
    }
    @{ Mark = $mark; Fail = $fail; Unver = $unver }
}

# 就绪探针：Shell（资源管理器有主窗口=桌面/任务栏起来了，keys/window/置前台才有意义）与网络（网卡可用）。
# 探测失败一律按「就绪」放行——绝不让探针自身故障把启动卡死。
function Test-ShellReady {
    try { return [bool](@(Get-Process explorer -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero }).Count) }
    catch { return $true }
}
function Test-NetworkReady {
    try { return [System.Net.NetworkInformation.NetworkInterface]::GetIsNetworkAvailable() }
    catch { return $true }
}

# 就绪门控：登录后环境未就绪时轮询等待，「就绪即返回」而非傻等固定时长——热启动/手动几乎零等待，冷启动/更新日才多等。
# $TimeoutSeconds 为总封顶，到点无论是否就绪都放行（绝不无限等）。探针/睡眠可注入，便于确定性测试（不依赖真实时钟/真机）。
# 返回 @{ Ready; WaitedMs; Shell; Net }。
function Wait-SystemReady {
    param(
        [int]$TimeoutSeconds = 90,
        [bool]$RequireNetwork = $true,
        [int]$PollMs = 500,
        [scriptblock]$ShellProbe = $null,
        [scriptblock]$NetProbe = $null,
        [scriptblock]$Sleeper = $null
    )
    if (-not $ShellProbe) { $ShellProbe = { Test-ShellReady } }
    if (-not $NetProbe)   { $NetProbe   = { Test-NetworkReady } }
    if (-not $Sleeper)    { $Sleeper    = { param($ms) Start-Sleep -Milliseconds $ms } }
    if ($PollMs -lt 1) { $PollMs = 500 }
    $maxWaitMs = [Math]::Max(0, $TimeoutSeconds * 1000)
    $shellOk = $false
    $netOk   = -not $RequireNetwork
    $waited  = 0
    while ($true) {
        if (-not $shellOk) { try { $shellOk = [bool](& $ShellProbe) } catch { $shellOk = $true } }
        if (-not $netOk)   { try { $netOk   = [bool](& $NetProbe) }   catch { $netOk = $true } }
        if ($shellOk -and $netOk) { break }   # 就绪即走
        if ($waited -ge $maxWaitMs) { break } # 到封顶：放行（宁可早跑也不挂死）
        if (Test-StopRequested) { break }     # 急停：不再干等就绪，放行后由启动序列的停止检查中止
        & $Sleeper $PollMs
        $waited += $PollMs
    }
    [pscustomobject]@{ Ready = ($shellOk -and $netOk); WaitedMs = $waited; Shell = $shellOk; Net = $netOk }
}

# $Boot：仅开机自启路径传 $true —— 此时才做「就绪门控 + 可配延迟」（登录后环境未就绪）。
# 手动「重新运行」不传：环境本就就绪，立即跑、不等待、不延迟。
function Invoke-LaunchSequence {
    param($Config, [string]$LogPath, [switch]$Boot)
    [void](Confirm-Win32Available)   # 懒编译 SH.Native/SH.Audio（受限令牌下编译失败会降级、不抛）；仅启动序列需要
    $bootNote = $null
    $stopped = $false   # 急停标志：Test-StopRequested/可中断延时一旦响应即置位，跳过所有剩余步骤、如实进日志
    if ($Boot -and $Config.settings) {
        # 可选就绪门控（默认关）：等 Shell/网络就绪，就绪即走、封顶 90s 兜底。仅当 settings.startupWaitForReady=true 才启用。
        # 默认不开——它测「壳/网存不存在」，冷启动一两秒就过，不反映机器是否闲下来；主延时交给下面的固定缓冲。
        $waitReady = $false
        if ($Config.settings.PSObject.Properties['startupWaitForReady']) { $waitReady = [bool]$Config.settings.startupWaitForReady }
        if ($waitReady) {
            $r = Wait-SystemReady
            $bootNote = "就绪门控：等待 {0:N1}s（Shell={1} 网络={2}）{3}" -f ($r.WaitedMs / 1000), $r.Shell, $r.Net, $(if (-not $r.Ready) { '，超时仍未就绪，照常放行' } else { '' })
        }
        # 诚实固定延时（主杠杆）：从被唤醒起真实等待 N 秒再跑清单。原「计划任务 15s 触发延迟 + 就绪门控」都并入这一个可调数字，
        # 慢机器/登录风暴重就把它调大（GUI 0–600）。这是唯一稳定生效、可预测的延时。可被急停打断（开机不想跑清单时按急停键即弃跑）。
        $preDelay = [int]$Config.settings.startupDelaySeconds
        if ($preDelay -gt 0) {
            $bootNote = (($bootNote, ("开机延迟：{0}s" -f $preDelay)) | Where-Object { $_ }) -join '；'
            if (-not (Start-InterruptibleSleep ($preDelay * 1000))) { $stopped = $true }
        }
    }
    # 小时/星期取一次、全程共用：带延时的长序列跨点时，顶层与组内子步骤的「仅N点前/仅星期」判定基准一致
    $nowHour = [int](Get-Date).Hour
    $nowIso  = [int](Get-Date).DayOfWeek; if ($nowIso -eq 0) { $nowIso = 7 }
    $plan = Build-LaunchPlan $Config $nowHour $nowIso
    $lines = New-Object System.Collections.ArrayList
    $fail = 0; $unver = 0; $total = 0
    foreach ($step in $plan) {
        if (-not $stopped -and (Test-StopRequested)) { $stopped = $true }
        if ($stopped) { break }
        $ts = (Get-Date).ToString('HH:mm:ss')
        # 循环动作：整个步骤（含 group 展开）重复 repeat 次；每次之间等 delayMs（末次由步尾统一延时负责）。
        $rep = Get-StepRepeat $step
        if ([string]$step.kind -eq 'group') {
            # group 步骤：解析引用的动作组，展开其非 message 步骤逐条进日志（诚实三态）。
            # message 步骤在启动展开时跳过（启动静默，不宜中途弹确认框）。
            $g = Resolve-ActionGroup $Config.actionGroups ([string]$step.groupId)
            if (-not $g) {
                [void]$lines.Add(("[{0}] {1}  ⚠ 找不到动作组" -f $ts, (Get-StepSummary $step))); $fail++; $total++
            } elseif (-not $g.enabled) {
                # 组被禁用：引用时如实跳过（区别于「找不到」），不计成败、不计入步数。
                [void]$lines.Add(("[{0}] {1}  · 动作组「{2}」已禁用，跳过" -f $ts, (Get-StepSummary $step), [string]$g.name))
            } else {
                for ($gi = 1; $gi -le $rep -and -not $stopped; $gi++) {
                    $gHdr = if ($rep -gt 1) { "运行动作组：{0}（第 {1}/{2} 次）" -f [string]$g.name, $gi, $rep } else { "运行动作组：{0}" -f [string]$g.name }
                    [void]$lines.Add(("[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), $gHdr))
                    foreach ($sub in @($g.steps)) {
                        if (-not $stopped -and (Test-StopRequested)) { $stopped = $true }
                        if ($stopped) { break }
                        if (-not $sub.enabled) { continue }
                        if (-not (Test-StepCondition $sub $nowHour $nowIso)) { continue }   # 组内步骤同样遵守时间条件（与顶层同基准）
                        if ([string]$sub.kind -eq 'message') { continue }
                        $subRep = Get-StepRepeat $sub
                        for ($si = 1; $si -le $subRep -and -not $stopped; $si++) {
                            $rr = Get-StepRunMark $sub
                            $subSfx = if ($subRep -gt 1) { "（第 $si/$subRep 次）" } else { '' }
                            [void]$lines.Add(("[{0}]     {1}{2}  {3}" -f (Get-Date).ToString('HH:mm:ss'), (Get-StepSummary $sub), $subSfx, $rr.Mark))
                            $fail += $rr.Fail; $unver += $rr.Unver; $total++
                            if (Test-StopRequested) { $stopped = $true }
                            elseif ([int]$sub.delayMs -gt 0) { if (-not (Start-InterruptibleSleep ([int]$sub.delayMs))) { $stopped = $true } }
                        }
                    }
                    if (-not $stopped -and $gi -lt $rep -and [int]$step.delayMs -gt 0) { if (-not (Start-InterruptibleSleep ([int]$step.delayMs))) { $stopped = $true } }
                }
            }
        } else {
            for ($i = 1; $i -le $rep -and -not $stopped; $i++) {
                $rr = Get-StepRunMark $step
                $sfx = if ($rep -gt 1) { "（第 $i/$rep 次）" } else { '' }
                [void]$lines.Add(("[{0}] {1}{2}  {3}" -f (Get-Date).ToString('HH:mm:ss'), (Get-StepSummary $step), $sfx, $rr.Mark))
                $fail += $rr.Fail; $unver += $rr.Unver; $total++
                if (Test-StopRequested) { $stopped = $true }
                elseif ($i -lt $rep -and [int]$step.delayMs -gt 0) { if (-not (Start-InterruptibleSleep ([int]$step.delayMs))) { $stopped = $true } }
            }
        }
        if (-not $stopped -and [int]$step.delayMs -gt 0) { if (-not (Start-InterruptibleSleep ([int]$step.delayMs))) { $stopped = $true } }
    }
    if ($stopped) { [void]$lines.Add(("[{0}] ⏹ 已手动停止，后续步骤未执行" -f (Get-Date).ToString('HH:mm:ss'))) }
    if ($LogPath) {
        try {
            $bootHdr = if ($bootNote) { "$bootNote`r`n" } else { '' }
            $stopHdr = if ($stopped) { "⏹ 本次运行被手动停止（急停键 / 托盘「停止」）`r`n" } else { '' }
            $hdr = "Clockwork · 上次启动清单运行日志`r`n时间：{0}`r`n{1}{2}共 {3} 步：{4} 步失败/警告、{5} 步已发送但无法校验、其余成功`r`n（~ 表示按键/热键类动作已注入，但目标是否响应无法确认）`r`n{6}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $bootHdr, $stopHdr, $total, $fail, $unver, ('=' * 40)
            Set-Content -LiteralPath $LogPath -Value (@($hdr) + $lines) -Encoding UTF8
        } catch {}
    }
    [pscustomobject]@{ Total = $total; Fail = $fail; Unverified = $unver; Stopped = $stopped }
}

# 在后台 runspace 跑 $Script，完成后由 UI 线程计时器调用 $OnDone（参数=脚本输出）。
# 计时器钉入 $script:BgTimers 防 GC（运行中的 Forms.Timer 仅靠自引用会被回收、不再 tick）。
# $Vars 注入子 runspace；-STA 用于序列里要 SendKeys/置前台的场景。启动序列、系统启动项枚举、
# 开机自启状态查询都走它，避免在 UI 线程上卡住。
function Invoke-InRunspaceAsync {
    param([scriptblock]$Script, [hashtable]$Vars, [scriptblock]$OnDone, [switch]$STA)
    if (-not $script:BgTimers) { $script:BgTimers = New-Object System.Collections.ArrayList }
    $bag = $script:BgTimers
    $rs = $null; $ps = $null
    try {
        $rs = [runspacefactory]::CreateRunspace()
        if ($STA) { $rs.ApartmentState = [System.Threading.ApartmentState]::STA }
        $rs.Open()
        if ($Vars) { foreach ($k in $Vars.Keys) { $rs.SessionStateProxy.SetVariable($k, $Vars[$k]) } }
        $ps = [powershell]::Create(); $ps.Runspace = $rs
        [void]$ps.AddScript($Script)
        $async = $ps.BeginInvoke()
        # 轮询定时器可插拔：WPF 版注入 $script:MakeAsyncTimer 返回 DispatcherTimer（WinForms.Timer 在 WPF 消息循环下不可靠）；
        # 未注入时退回 WinForms.Timer（WinForms 版原样）。二者都有 Add_Tick/Start/Stop，Interval 由工厂各自设好。
        $t = if ($script:MakeAsyncTimer) { & $script:MakeAsyncTimer } else { $wt = New-Object System.Windows.Forms.Timer; $wt.Interval = 400; $wt }
        [void]$bag.Add($t)
        $t.Add_Tick({
            if ($async.IsCompleted) {
                $t.Stop()
                $out = $null
                try { $out = $ps.EndInvoke($async) } catch {}
                try { $rs.Dispose() } catch {}
                $ps.Dispose(); try { $t.Dispose() } catch {}; [void]$bag.Remove($t)
                if ($OnDone) { & $OnDone $out }
            }
        }.GetNewClosure())
        $t.Start()
    } catch {
        # 建 runspace / Open / BeginInvoke 阶段抛错（资源紧张等）：计时器没起来 → OnDone 永不会被调度 →
        # 调用方的「运行中」标志(LaunchState/ActionGroupRunning/StepActionState/ReminderFiring)永远清不掉，
        # 而 Test-AnyRunActive 读这些标志门控急停信号的清理 —— 一处泄漏就把急停永久卡死（此后每次运行都秒停，只能重启）。
        # 兜底：清理已建对象，并以 $null 调一次 OnDone，让调用方走它的收尾（各 OnDone 都先清标志再用输出、容忍 $null）。
        try { if ($ps) { $ps.Dispose() } } catch {}
        try { if ($rs) { $rs.Dispose() } } catch {}
        Write-Warning "后台任务启动失败：$($_.Exception.Message)"
        if ($OnDone) { try { & $OnDone $null } catch {} }
    }
}

# 是否有【任何】执行在跑（启动序列/动作组/单步/提醒）。急停信号是全局粘滞的，新一轮开跑前须复位——但【只在
# 完全空闲时】复位：否则一个并发启动的操作会把刚为另一在跑操作按下的急停擦掉，急停被静默吞掉。
# 前三个守卫标志由主 runspace 的 Async 入口维护、彼此可见。提醒在【背景 runspace】里跑、不设这些标志，故【必须】
# 额外看 $script:ReminderFiring（提醒计时器在主 runspace 维护：派发时置、OnDone 移除）——否则「提醒组正在被急停、
# 用户又从 UI 起个动作」会经 Reset-StopIfIdle 擦掉那次急停（评审确认：不能只给心跳加 $firing 感知而漏了本函数）。
# 注：Reset-StopIfIdle 只从主 runspace 调用（见三个 Async 入口，均在 AppRoot 判断之后），故此处 $script 均权威可读。
function Test-AnyRunActive {
    if ($script:LaunchState -and $script:LaunchState.Running) { return $true }
    if ($script:StepActionState -and $script:StepActionState.Running) { return $true }
    if ($script:ActionGroupRunning) { foreach ($v in $script:ActionGroupRunning.Values) { if ($v) { return $true } } }
    if ($script:ReminderFiring -and $script:ReminderFiring.Count -gt 0) { return $true }
    $false
}
# 仅在无任何运行在跑时才复位急停信号（见 Test-AnyRunActive）。有运行在跑时保留信号，避免并发操作吞掉急停。
function Reset-StopIfIdle { if (-not (Test-AnyRunActive)) { Clear-StopAll } }

# 手动「重新运行」/开机自启序列：后台跑，避免 Sleep/微信前台等待冻结 UI。并发守卫防连点交错。
function Invoke-LaunchSequenceAsync {
    param($Config, $Tray, [switch]$Boot)   # $Tray 给了就在完成时弹托盘气泡（手动重跑用；开机自启不传=静默）。$Boot：开机自启路径才做就绪门控/延迟。
    if (-not $script:LaunchState) { $script:LaunchState = @{ Running = $false } }
    if ($script:LaunchState.Running) { return }   # 已有一次在跑，忽略重复触发（避免交错按键/重复启动）
    if (-not $script:AppRoot) { Invoke-LaunchSequence $Config -Boot:$Boot; return }   # 兜底：无根路径则同步（测试）
    Reset-StopIfIdle   # 仅主 runspace（AppRoot 已设=UI 线程）路径复位：此处三个 Running 守卫可见、能判是否全空闲。
                       # 背景 runspace（AppRoot 未设，见下方 Async 里）看不到这些守卫，故绝不在那里复位，否则会擦掉别处的急停。
    $script:LaunchState.Running = $true
    $state = $script:LaunchState; $tray2 = $Tray   # 本地引用供 OnDone 闭包捕获
    Invoke-InRunspaceAsync -STA `
        -Vars @{ appRoot = $script:AppRoot; cfgJson = ($Config | ConvertTo-Json -Depth 8); boot = [bool]$Boot } `
        -OnDone ({ param($out)
            $state.Running = $false
            if ($tray2) {
                $sum = $out | Select-Object -Last 1
                if (-not $sum -or $null -eq $sum.Total) {   # 后台 runspace 异常/无输出：如实提示而非显示成「：步」
                    Show-TrayNotify $tray2 'Clockwork' '启动清单执行异常结束（详见启动日志）'
                    return
                }
                # 被急停：如实告知停在哪（比「有警告」优先——停止是用户主动行为，必须给回执）。
                if ($sum.PSObject.Properties['Stopped'] -and [bool]$sum.Stopped) {
                    Show-TrayNotify $tray2 'Clockwork' "启动清单已手动停止：已执行 $($sum.Total) 步，其余未执行"
                    return
                }
                # 只在真失败/警告时弹：手动重跑时人就在跟前，程序逐个打开即是成功确认，「全部正常」纯噪音。
                # 完整三态(✓/⚠/~)仍写入启动日志，静默不丢信息（~ 已发送未校验不算失败，一并静默）。
                if ([int]$sum.Fail -gt 0) {
                    Show-TrayNotify $tray2 'Clockwork' "启动清单执行完成：$($sum.Total) 步，$($sum.Fail) 步有警告（右键托盘→查看上次启动日志）"
                }
            }
        }.GetNewClosure()) `
        -Script {
            Add-Type -AssemblyName System.Windows.Forms
            . (Join-Path $appRoot 'lib\Clockwork.Core.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Win32.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Actions.ps1')
            $script:LaunchSelfPaths = @((Join-Path $appRoot 'clockwork.ps1'), (Join-Path $appRoot 'Clockwork.bat'))
            Invoke-LaunchSequence -Config ($cfgJson | ConvertFrom-Json) -LogPath (Join-Path $appRoot 'clockwork.run.log') -Boot:([bool]$boot)
        }
}

function Invoke-ReminderAction {
    param($OnYes, $Groups = @())
    if ($null -eq $OnYes) { return }
    # 与 Invoke-LaunchItem 一致：目标为空/文件已删/路径无效时 Start-Process 抛【终止性】错误，
    # 此函数经 Invoke-Reminder 在计时器 tick 内调用，未捕获会冒泡成全局未处理异常框。失败仅记 Warning。
    try {
        # 'sound' 是历史类型：实现与「运行程序/打开文件」完全相同（Start-Process 走文件关联），已从界面移除、
        # 此处按 'run' 处理以兼容旧配置。
        $yType = [string]$OnYes.type; if ($yType -eq 'sound') { $yType = 'run' }
        switch ($yType) {
            'run' {
                if ($OnYes.target -match '\.ps1$') { Start-Process powershell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $OnYes.target + '"')) | Out-Null }
                else { Start-Process $OnYes.target | Out-Null }
            }
            'url'   { Start-Process $OnYes.target | Out-Null }
            'group' {
                $g = Resolve-ActionGroup $Groups ([string]$OnYes.target)
                if (-not $g)             { Write-Warning "提醒引用的动作组不存在（id=$($OnYes.target)）" }
                elseif (-not $g.enabled) { Write-Warning "动作组「$($g.name)」已禁用，跳过" }
                else                     { Invoke-ActionGroupAsync $g }
            }
            default { }
        }
    } catch {
        Write-Warning "提醒确认动作失败：$($_.Exception.Message)"
    }
}

# 后台朗读：用 SAPI 自带的异步标志（SVSFlagsAsync=1）——立即返回、后台发声，任何线程/runspace 都可用，
# 不再自旋 runspace（旧方案在无消息泵的子 runspace 里同步兜底，长文本会把弹窗拖后好几秒）。
# 语音对象按会话缓存复用；提醒/动作组的子 runspace 结束前需 Wait-SpeakDone，否则 COM 随 runspace 释放会掐断朗读。
function Start-SpeakAsync {
    param([string]$Text)
    try {
        if (-not $script:SpVoice) { $script:SpVoice = New-Object -ComObject SAPI.SpVoice }
        $script:SpVoice.Speak([string]$Text, 1) | Out-Null   # 1 = SVSFlagsAsync
    } catch { Write-Warning "语音播报失败：$($_.Exception.Message)" }
}

# 等当前朗读说完（子 runspace 收尾用；超时兜底防挂死）。主进程不需要调用。
function Wait-SpeakDone {
    param([int]$TimeoutMs = 60000)
    try { if ($script:SpVoice) { [void]$script:SpVoice.WaitUntilDone($TimeoutMs) } } catch {}
}

function Invoke-Reminder {
    param($Reminder, $Groups = @())
    # 静默运行动作组：到点不弹窗、忽略 message/confirm/onYes，直接跑指定组。
    if (-not [string]::IsNullOrWhiteSpace([string]$Reminder.silentGroupId)) {
        $g = Resolve-ActionGroup $Groups ([string]$Reminder.silentGroupId)
        if (-not $g)             { Write-Warning "提醒的静默动作组不存在（id=$($Reminder.silentGroupId)）" }
        elseif (-not $g.enabled) { Write-Warning "动作组「$($g.name)」已禁用，跳过" }
        else                     { Invoke-ActionGroupAsync $g }
        # 返回 ''（未确认）而非 'ok'：静默组若同时配了「重复催促」(repeatMinutes>0)，Update-ReminderAfterFire 才会按 repeatMinutes 排下次；
        # 原来的 'ok' 被当成「已确认」直接清掉重复计划，静默组只跑一次就再不重复。非重复情形返回 '' 同样正常收尾。
        return [pscustomobject]@{ Action = ''; SnoozeMinutes = $null }
    }
    if ($Reminder.speak) { Start-SpeakAsync ([string]$Reminder.message) }
    # 是/否只跟「点是后」走：配了动作 → 弹「是/否」问你（否则动作永远无法触发——旧逻辑还要求勾 confirm，
    # 漏勾就成死配置）；没配动作 → 只弹「确定」，省一次无意义的选择（配合「自动关闭」可完全免点击）。
    $confirm = [bool]($Reminder.onYes -and [string]$Reminder.onYes.type -ne 'none')
    # 无动作、且非重复催促的提醒走系统「日常通知」：不置顶抢视线、错过自动进通知中心。
    # 重复催促型（repeatMinutes>0）保留自绘弹窗——Toast 没有「确定=停止催促」「稍后」按钮，
    # 用户将无法停下每 N 分钟一条的轰炸。系统通知不可用（被用户关闭等）时也退回自绘弹窗。
    if (-not $confirm -and [int]$Reminder.repeatMinutes -le 0) {
        # Toast 时长：「自动关闭」>=20 秒 → 长通知(~25s)，否则短(~5-7s)。Windows 只这两档。
        if (Show-SystemToast ([string]$Reminder.message) '' (Test-ReminderToastLong $Reminder)) { return [pscustomobject]@{ Action=''; SnoozeMinutes=$null } }
    }
    # 自动关闭秒数：显式配置优先，否则重复型默认 60s，否则永不（见 Get-PopupTimeoutSeconds）。
    $autoDismiss = Get-PopupTimeoutSeconds $Reminder
    $p = Show-ReminderPopup ([string]$Reminder.message) $confirm $autoDismiss
    if ($p.Action -eq 'yes') { Invoke-ReminderAction $Reminder.onYes $Groups }
    # 「稍后」不在此处处理：返回完整结果(含 SnoozeMinutes)，由计时器 tick 调 Set-ReminderSnooze 钉一次性 snoozeUntil。
    $p
}

# 顺序执行一个动作组的步骤。message 步骤弹窗(隐藏稍后)：是=跑 onYes 后继续；否/关闭=中止整组；确定=继续。
# 步骤时间条件（仅星期/仅N点前）与顶层清单同样遵守（Test-StepCondition）。
# 并发守卫用【命名互斥锁】（按组 id）：托盘/提醒/启动清单各跑在不同 runspace，进程内 hashtable 守卫跨不了
# 上下文——两个静默提醒同 tick 触发同一组会双开每个程序、按键交错。互斥锁进程级全局，谁先到谁跑、后到者跳过。
function Invoke-ActionGroup {
    param($Group)
    $mtx = New-Object System.Threading.Mutex($false, ('Local\rockbenben.clockwork.group.' + [string]$Group.id))
    $got = $false
    try { $got = $mtx.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $got = $true }
    if (-not $got) { Write-Warning "动作组「$($Group.name)」已在运行，忽略本次触发"; $mtx.Dispose(); return }
    try {
        # 小时/星期在组开跑时取一次：跨点的长组内各步骤用同一判定基准（与顶层清单一致）
        $hour = [int](Get-Date).Hour
        $iso  = [int](Get-Date).DayOfWeek; if ($iso -eq 0) { $iso = 7 }
        $stopped = $false   # 急停：每步/每次循环/每段延时之间响应，收到即中止整组剩余部分
        foreach ($step in @($Group.steps)) {
            if ($stopped -or (Test-StopRequested)) { break }
            if (-not $step.enabled) { continue }
            if (-not (Test-StepCondition $step $hour $iso)) { continue }
            if ([string]$step.kind -eq 'message') {
                if ($step.speak) { Start-SpeakAsync ([string]$step.message) }
                # confirm 勾选=确认闸门（「是」继续、「否」中止整组，如收工前"记录好了吗？"）；
                # 配了 onYes 则强制弹「是/否」——否则动作永远无法触发（点「确定」返回 ok≠yes）。
                $confirm = [bool]($step.confirm -or ($step.onYes -and [string]$step.onYes.type -ne 'none'))
                $p = Show-ReminderPopup ([string]$step.message) $confirm 0 -NoSnooze
                if     ($p.Action -eq 'yes') { Invoke-ReminderAction $step.onYes }
                elseif ($p.Action -eq 'no')  { break }   # 否/关闭 -> 中止整组剩余步骤
                if ([int]$step.delayMs -gt 0) { if (-not (Start-InterruptibleSleep ([int]$step.delayMs))) { $stopped = $true } }
            } else {
                # 循环动作：重复 repeat 次，每次执行后等 delayMs（message 不循环——确认闸门弹 N 次无意义）。
                $rep = Get-StepRepeat $step
                for ($i = 1; $i -le $rep -and -not $stopped; $i++) {
                    [void](Invoke-StepAction $step)   # 丢弃成功流输出（keys/text/window-sendkey 的 'unverified'、app 的返回值等）——动作组只执行、不按步标记(那是启动路径 Get-StepRunMark 的事)，不丢会漏进本函数返回值
                    if (Test-StopRequested) { $stopped = $true }
                    elseif ([int]$step.delayMs -gt 0) { if (-not (Start-InterruptibleSleep ([int]$step.delayMs))) { $stopped = $true } }
                }
            }
        }
    } finally { try { $mtx.ReleaseMutex() } catch {}; $mtx.Dispose() }
}

# 顺序把动作组跑 $Repeat 遍，每遍之间等 $RepeatDelayMs（可被急停打断）。$Repeat 来自「动作组」步骤的 repeat，
# 单遍语义不变（默认 1）。整组循环放在这里而非组内：与启动序列 group 步骤的展开口径一致（组×N，非把每个子步骤各×N）。
function Invoke-ActionGroupRepeat {
    param($Group, [int]$Repeat = 1, [int]$RepeatDelayMs = 0)
    $rep = Get-ClampedRepeat $Repeat
    for ($i = 1; $i -le $rep; $i++) {
        if (Test-StopRequested) { break }
        Invoke-ActionGroup $Group
        if ($i -lt $rep -and [int]$RepeatDelayMs -gt 0) { if (-not (Start-InterruptibleSleep ([int]$RepeatDelayMs))) { break } }
    }
}

# 后台 STA runspace 跑动作组：window 动作的等窗口/发键、步骤 Sleep 都不冻 UI；message 弹窗在该 STA 线程 ShowDialog。
# 并发守卫按组 id：同组在跑则忽略重复触发。
# $Repeat/$RepeatDelayMs：整组循环次数与遍间延时——启动清单里「动作组」步骤设了 repeat 时，单步「运行」
# 预览要跑同样遍数才与开机行为一致（否则预览恒跑一遍，循环组步骤的测试按钮失真）。托盘/提醒触发默认 1，不变。
function Invoke-ActionGroupAsync {
    param($Group, [int]$Repeat = 1, [int]$RepeatDelayMs = 0)
    if (-not $script:ActionGroupRunning) { $script:ActionGroupRunning = @{} }
    $gid = [string]$Group.id
    if ($script:ActionGroupRunning[$gid]) { return }
    # 关键：AppRoot 未设=在【提醒的背景 runspace】里被调用（静默组/点是组走此路径）——此上下文里三个 $script
    # 守卫恒空、Test-AnyRunActive 恒 false，若在此复位急停信号会擦掉主线程正在跑的启动序列的急停（评审确认的 bug）。
    # 故背景/同步路径【绝不】复位；仅下面的主 runspace 路径复位。滞留的空闲急停由提醒计时器心跳在 UI 线程安全清理。
    if (-not $script:AppRoot) { Invoke-ActionGroupRepeat $Group $Repeat $RepeatDelayMs; return }
    Reset-StopIfIdle   # 仅主 runspace（UI 线程）路径：三个 Running 守卫可见、能判是否全空闲
    $script:ActionGroupRunning[$gid] = $true
    $running = $script:ActionGroupRunning
    Invoke-InRunspaceAsync -STA `
        -Vars @{ appRoot = $script:AppRoot; groupJson = ($Group | ConvertTo-Json -Depth 8); repeat = ([int]$Repeat); repeatDelayMs = ([int]$RepeatDelayMs) } `
        -OnDone ({ param($out) $running[$gid] = $false }.GetNewClosure()) `
        -Script {
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -AssemblyName System.Drawing
            Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
            . (Join-Path $appRoot 'lib\Clockwork.Core.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Win32.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Actions.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.WpfDialogs.ps1')   # 消息步骤弹窗 Show-ReminderPopup(WPF)
            [void](Confirm-Win32Available)
            # 自启动守卫与启动序列 runspace 一致：组内 app 步骤若指向Clockwork自身则跳过，防套娃拉起第二实例
            $script:LaunchSelfPaths = @((Join-Path $appRoot 'clockwork.ps1'), (Join-Path $appRoot 'Clockwork.bat'))
            Invoke-ActionGroupRepeat ($groupJson | ConvertFrom-Json) $repeat $repeatDelayMs
            Wait-SpeakDone   # 朗读未完前别让 runspace 释放（COM 随之释放会掐断语音）
        }
}

# 后台 STA runspace 跑【单个】启动步骤（启动清单「运行」用）：keys/window 需 STA 与前台，故与启动序列同规格。
# 与序列共享 Get-StepRunMark 的诚实三态标记；跑完弹托盘气泡把结果回给用户（单步动作多半无可见弹窗，需要反馈）。
# 手动运行语义：无视该项的 enabled 与时间条件（仅N点前/仅星期），要的就是「现在就跑这一步看看」——与提醒「运行」一致。
# group 类型不走此函数：调用方改用 Invoke-ActionGroupAsync 跑引用的动作组。
function Invoke-StepActionAsync {
    param($Step, $Tray)
    # 并发守卫：与 Invoke-LaunchSequenceAsync（$script:LaunchState）/Invoke-ActionGroupAsync（按组 id）一致。
    # 气泡反馈要几秒才出现，连点很自然：keys/window 步骤重叠会让两个 STA runspace 交错注入按键，
    # 或把 app 步骤启动两次。已有一步在跑则忽略重复触发。
    if (-not $script:StepActionState) { $script:StepActionState = @{ Running = $false } }
    if ($script:StepActionState.Running) { return }
    if (-not $script:AppRoot) {   # 兜底：无根路径则同步跑（阻塞 UI，天然不会重入），仍尽量给反馈。不复位急停（同 Async 里的说明）
        $rr = Get-StepRunMarkRepeat $Step
        if ($Tray) { Show-TrayNotify $Tray 'Clockwork · 运行' ((Get-StepSummary $Step) + '  ' + $rr.Mark) }
        return
    }
    Reset-StopIfIdle   # 仅主 runspace（UI 线程，此函数只从 LTest 调）路径复位：三个 Running 守卫可见、能判是否全空闲
    $script:StepActionState.Running = $true
    $state = $script:StepActionState   # 本地引用供 OnDone 闭包捕获
    $tray2 = $Tray
    Invoke-InRunspaceAsync -STA `
        -Vars @{ appRoot = $script:AppRoot; stepJson = ($Step | ConvertTo-Json -Depth 8) } `
        -OnDone ({ param($out)
            $state.Running = $false
            if ($tray2) {
                $r = $out | Select-Object -Last 1
                if ($r -and $r.Mark) { Show-TrayNotify $tray2 'Clockwork · 运行' ([string]$r.Summary + '  ' + [string]$r.Mark) }
                else { Show-TrayNotify $tray2 'Clockwork · 运行' '运行异常结束（无输出）' }
            }
        }.GetNewClosure()) `
        -Script {
            Add-Type -AssemblyName System.Windows.Forms
            . (Join-Path $appRoot 'lib\Clockwork.Core.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Win32.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Actions.ps1')
            [void](Confirm-Win32Available)
            # 与启动序列一致：app 步骤若指向Clockwork自身则跳过，防套娃
            $script:LaunchSelfPaths = @((Join-Path $appRoot 'clockwork.ps1'), (Join-Path $appRoot 'Clockwork.bat'))
            $s  = $stepJson | ConvertFrom-Json
            $rr = Get-StepRunMarkRepeat $s   # 循环动作：手动运行也按 repeat 跑（测的就是循环本身），Mark 归纳
            [pscustomobject]@{ Summary = (Get-StepSummary $s); Mark = $rr.Mark }
        }
}

function Get-AutostartTaskName { 'Clockwork' }

function Test-AutostartRegistered {
    # 用 schtasks.exe 查询：比 Get-ScheduledTask 的 CIM 快很多，且在 Task Scheduler/CIM 卡顿的机器上不会挂起
    # （曾见某些机器上 Get-ScheduledTask 卡数分钟，拖住托盘菜单标签/检测）。
    try { & schtasks.exe /query /tn (Get-AutostartTaskName) 2>&1 | Out-Null; return ($LASTEXITCODE -eq 0) }
    catch { return $false }
}

# 注册「最高权限」任务必须由管理员进程发起；非提权时 Register-ScheduledTask 抛
# 「拒绝访问」（且是非终止错误，不加 -ErrorAction Stop 会被吞，调用方误以为成功）。
# 返回 'Ok' / 'NeedsAdmin' / 'Error: ...'，与 Set-SystemStartupItemEnabled 一致，便于 UI 提权重开。
# 用 schtasks.exe + XML 建任务（不走 ScheduledTasks/CIM——它在某些机器上会卡数分钟）。
# 命令写在 XML 的 <Arguments> 文本里，天然避开 /tr 的引号地狱。非提权时 schtasks 会「快速」拒绝（返回
# NeedsAdmin，而非 CIM 那样长时间无响应）。经 conhost --headless：避免 Win11 终端登录时留可见窗口。
# LogonTrigger 不带触发延迟：登录即唤醒本程序，让它先入托盘待命；真正「等登录风暴过峰再跑清单」的延时统一在
# 进程内做（见 Invoke-LaunchSequence 的 startupDelaySeconds），改数字即调、无需重注册任务——一个说话算数的杠杆。
function Register-Autostart {
    param([string]$ScriptPath)
    $name = Get-AutostartTaskName
    $user = "$env:USERDOMAIN\$env:USERNAME"
    $argsXml = [System.Security.SecurityElement]::Escape(('--headless powershell -NoProfile -ExecutionPolicy Bypass -File "{0}" -Run' -f $ScriptPath))
    $userXml = [System.Security.SecurityElement]::Escape($user)
    $xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Clockwork 登录自启</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>$userXml</UserId></LogonTrigger></Triggers>
  <Principals><Principal id="Author"><UserId>$userXml</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author"><Exec><Command>conhost.exe</Command><Arguments>$argsXml</Arguments></Exec></Actions>
</Task>
"@
    $tmp = Join-Path $env:TEMP ('shtask-' + [guid]::NewGuid().ToString('N') + '.xml')
    try {
        [System.IO.File]::WriteAllText($tmp, $xml, [System.Text.UnicodeEncoding]::new($false, $true))   # UTF-16 + BOM，schtasks 要求
        $out = & schtasks.exe /create /tn $name /xml $tmp /f 2>&1
        if ($LASTEXITCODE -eq 0) { return 'Ok' }
        if ("$out" -match 'denied|Access is denied|0x80070005|拒绝|权限') { return 'NeedsAdmin' }
        return "Error: $($out -join ' ')"
    } catch { return "Error: $($_.Exception.Message)" }
    finally { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
}

function Unregister-Autostart {
    if (-not (Test-AutostartRegistered)) { return 'Ok' }   # 幂等：本就没有 = 已是目标态
    $out = & schtasks.exe /delete /tn (Get-AutostartTaskName) /f 2>&1
    if ($LASTEXITCODE -eq 0) { return 'Ok' }
    if ("$out" -match 'denied|Access is denied|0x80070005|拒绝|权限') { return 'NeedsAdmin' }
    return "Error: $($out -join ' ')"
}
