# StartupHelper.Core.ps1 —— 纯逻辑，不引用 WinForms / Win32，可被测试 dot-source

function New-LaunchStep {
    param([string]$Kind, [hashtable]$Props = @{})
    $s = [ordered]@{
        enabled=$true; kind=$Kind; label=''; delayMs=0
        target=''; args=''; workDir=''; elevated=$false   # app
        combo=''                                            # keys
        groupId=''                                          # group（引用动作组 id）
        action=''; level=50; onlyBefore8=$false; beforeHour=8  # volume/window 共用 action；时间条件「仅 N 点前」
        days=@()                                            # 仅在这些星期(ISO 1..7)开机启动；空=每天
        process=''; sendKey='{ENTER}'                       # window
        waitForWindowSeconds=0; postWindowDelaySeconds=0    # window：等目标窗口出现的秒数(0=不等)、窗口出现后再等的秒数(给自动登录/主窗切换留时间)
        command=''                                          # system
        message=''; speak=$false; confirm=$false; onYes=@{ type='none'; target='' }  # message 步骤(动作组用)
    }
    foreach ($k in $Props.Keys) { $s[$k] = $Props[$k] }
    [pscustomobject]$s
}

# 在 $Arr 的「第 $Index 项之后」插入 $Item；$Index<0 或越界（无选中）则追加到末尾。
# 返回 @{ Items=新数组; NewIndex=新项落点 }——供「新增即选中并滚动到」用。用 ArrayList.Insert 而非数组切片，
# 避开 PS 里 $a[($i+1)..($n-1)] 在 i 为末项时区间反向的坑。
function Add-ItemAfter {
    param($Arr, $Item, [int]$Index)
    $l = New-Object System.Collections.ArrayList
    if ($null -ne $Arr) { [void]$l.AddRange([object[]]@($Arr)) }
    $pos = if ($Index -ge 0 -and $Index -lt $l.Count) { $Index + 1 } else { $l.Count }
    [void]$l.Insert($pos, $Item)
    [pscustomobject]@{ Items = $l.ToArray(); NewIndex = $pos }
}

# 拆 Run 键命令行 -> @{Target;Arguments}。首字符为引号则取引号内为 Target；否则第一个空白前为 Target。
function ConvertFrom-CommandLine {
    param([string]$CommandLine)
    $s = [string]$CommandLine
    if ([string]::IsNullOrWhiteSpace($s)) { return [pscustomobject]@{ Target=''; Arguments='' } }
    $s = $s.Trim()
    if ($s[0] -eq '"') {
        $end = $s.IndexOf('"', 1)
        if ($end -lt 0) { return [pscustomobject]@{ Target=$s.Trim('"'); Arguments='' } }
        $target = $s.Substring(1, $end - 1)
        $rest = $s.Substring($end + 1).Trim()
    } else {
        $idx = $s.IndexOfAny([char[]]@(' ', "`t"))
        if ($idx -lt 0) { $target = $s; $rest = '' }
        else { $target = $s.Substring(0, $idx); $rest = $s.Substring($idx + 1).Trim() }
    }
    [pscustomobject]@{ Target=$target; Arguments=$rest }
}

# 据系统启动项 item 构造「接管」用的 app 启动步骤（默认延迟 2000ms，体现接管价值）。
function New-ImportedLaunchStep {
    param($Item)
    if ([string]$Item.type -eq 'StartupFolder') {
        return New-LaunchStep 'app' @{ label=[string]$Item.name; target=[string]$Item.command; args=''; delayMs=2000; enabled=$true }
    }
    $p = ConvertFrom-CommandLine ([string]$Item.command)
    New-LaunchStep 'app' @{ label=[string]$Item.name; target=$p.Target; args=$p.Arguments; delayMs=2000; enabled=$true }
}

function Get-DefaultLaunchSteps {
    # 首次使用的示例清单：不含个人路径，演示各类步骤，首次使用请按需替换 / 增删为你自己的软件。
    # 前 8 条默认勾选、开机会真的执行（一套克制的「早晨例程」）；后 3 条默认「不勾选」，只作各类步骤的
    # 演示样例——避免设为开机自启后每天弹出「设置 / 任务管理器」等噪声。想用哪条就勾上、不想要就删掉。
    @(
        New-LaunchStep 'volume' @{ label='开机先静音（示例·仅上午 8 点前生效，晚上开机就不会突然出声）'; action='mute'; onlyBefore8=$true }
        New-LaunchStep 'app'    @{ label='打开浏览器（示例·换成你常用的浏览器）'; target='msedge.exe' }
        New-LaunchStep 'app'    @{ label='打开常用网站（示例·换成你的邮箱 / 待办 / 网页版应用）'; target='https://github.com'; delayMs=800 }
        New-LaunchStep 'app'    @{ label='打开常用软件（示例·把目标换成你自己的 .exe 或快捷方式）'; target='notepad.exe'; delayMs=1000 }
        New-LaunchStep 'delay'  @{ label='等待 2 秒（示例·纯延时步骤，给前面的软件留出打开时间）'; delayMs=2000 }
        New-LaunchStep 'keys'   @{ label='回到桌面 Win+D（示例·发送组合键）'; combo='Win+D' }
        New-LaunchStep 'volume' @{ label='音量调到 30%（示例·设音量会自动取消上面的静音）'; action='set'; level=30; delayMs=500 }
        New-LaunchStep 'window' @{ label='最小化浏览器（示例·窗口动作，按进程名操作）'; action='minimize'; process='msedge'; delayMs=1000 }
        New-LaunchStep 'system' @{ label='显示桌面（示例·系统命令，默认不勾选）'; command='showDesktop'; enabled=$false }
        New-LaunchStep 'app'    @{ label='Windows 设置（示例·可直接打开 URI 协议，默认不勾选）'; target='ms-settings:'; enabled=$false }
        New-LaunchStep 'app'    @{ label='任务管理器（示例·默认不勾选，避免每次开机弹出）'; target='taskmgr.exe'; enabled=$false }
    )
}

function New-Reminder {
    param([hashtable]$Props = @{})
    $r = [ordered]@{
        id=[guid]::NewGuid().ToString()   # 稳定身份：计时器运行时状态(snoozeUntil/lastFiredDate…)按它做键，改文案/同名同时刻不串状态
        enabled=$true; trigger='time'; time='09:00'; days=@()
        message=''; speak=$false; onYes=@{ type='none'; target='' }   # confirm 已废弃：是/否只由 onYes 决定（见 Invoke-Reminder）
        graceMinutes=5; delaySeconds=0; randomDelaySeconds=0; repeatMinutes=0; repeatUntil=''
        recurType='daily'; intervalDays=1; monthlyDay=1; anchorDate=''
        popupTimeoutSeconds=0; startupHourMode='any'; startupHour=9
        startupWithinMinutes=10   # 「登录时」只认真正的开机时段：开机超过 N 分钟后再启动本程序不算登录（0=每次启动都算）
        silentGroupId=''   # 非空=到点静默(不弹窗)运行该动作组
    }
    foreach ($k in $Props.Keys) { $r[$k] = $Props[$k] }
    [pscustomobject]$r
}

function New-ActionGroup {
    param([hashtable]$Props = @{})
    $g = [ordered]@{
        id=[guid]::NewGuid().ToString()   # 稳定 id：托盘映射 + 引用解析
        name=''; enabled=$true; steps=@()
    }
    foreach ($k in $Props.Keys) { $g[$k] = $Props[$k] }
    [pscustomobject]$g
}

# 常用动作组模板（每次调用现生成 → 各自新 id，重复添加不撞 id）。用最普遍的默认进程名（微信 Weixin / QQ），
# 面向大众常见习惯；添加后按你自己的软件改进程名即可。
function Get-ActionGroupTemplates {
    @(
        New-ActionGroup @{ name='专注·开始工作'; steps=@(
            (New-LaunchStep 'window' @{ action='close'; process='Weixin'; label='关闭 微信' }),
            (New-LaunchStep 'window' @{ action='close'; process='QQ'; label='关闭 QQ' }),
            (New-LaunchStep 'volume' @{ action='mute'; label='系统静音' }),
            (New-LaunchStep 'system' @{ command='showDesktop'; label='显示桌面' }) ) }
        New-ActionGroup @{ name='会议模式'; steps=@(
            (New-LaunchStep 'volume' @{ action='mute'; label='系统静音' }),
            (New-LaunchStep 'window' @{ action='close'; process='Weixin'; label='关闭 微信' }),
            (New-LaunchStep 'window' @{ action='close'; process='QQ'; label='关闭 QQ' }) ) }
        New-ActionGroup @{ name='收工·下班'; steps=@(
            (New-LaunchStep 'message' @{ message='今天的任务都记录好了吗？'; confirm=$true }),
            (New-LaunchStep 'window' @{ action='close'; process='Weixin'; label='关闭 微信' }),
            (New-LaunchStep 'window' @{ action='close'; process='QQ'; label='关闭 QQ' }),
            (New-LaunchStep 'system' @{ command='emptyRecycleBin'; label='清空回收站' }),
            (New-LaunchStep 'system' @{ command='clearClipboard'; label='清空剪贴板' }),
            (New-LaunchStep 'system' @{ command='lockScreen'; label='锁屏' }) ) }
        New-ActionGroup @{ name='睡前'; steps=@(
            (New-LaunchStep 'message' @{ message='该睡觉了！'; speak=$true }),
            (New-LaunchStep 'volume' @{ action='mute'; label='系统静音' }),
            (New-LaunchStep 'window' @{ action='close'; process='Weixin'; label='关闭 微信' }),
            (New-LaunchStep 'system' @{ command='monitorOff'; label='关闭显示器' }) ) }
        New-ActionGroup @{ name='离开一下'; steps=@(
            (New-LaunchStep 'system' @{ command='lockScreen'; label='锁屏' }),
            (New-LaunchStep 'system' @{ command='monitorOff'; label='关闭显示器' }) ) }
        New-ActionGroup @{ name='截图标注'; steps=@(
            (New-LaunchStep 'system' @{ command='screenshot'; label='截图 Win+Shift+S' }),
            (New-LaunchStep 'app' @{ target='mspaint.exe'; label='打开 画图'; delayMs=800 }) ) }
    )
}

# 按 id 在动作组列表里解析出组；空 id / 未命中 / 空列表 → $null。启动步骤与提醒引用组时共用。
function Resolve-ActionGroup {
    param($Groups, [string]$Id)
    if ([string]::IsNullOrWhiteSpace($Id)) { return $null }
    foreach ($g in @($Groups)) { if ($g -and ([string]$g.id -eq $Id)) { return $g } }
    $null
}

# 反序列化「JSON 数组」为扁平 PS 数组——跨 runspace 传动作组/步骤列表时必用。
# 坑：Windows PowerShell 5.1 的 ConvertFrom-Json 把 JSON 数组当【单个对象】发到管道（PS7 才逐元素展开），
# 于是 @($json | ConvertFrom-Json) 会把整个数组再套一层 → 变成「1 个元素(该数组)」，
# 之后 Resolve-ActionGroup 按 id 遍历时遇到的是内层数组、匹配不到，表现为「提醒点『是』运行动作组无反应」。
# 正解：先赋值拿到原生数组，再对【变量】做 @() 归一化（对变量的 @() 是逐元素、不套层）。两版 PS 结果一致。
function ConvertFrom-JsonArray {
    param([string]$Json)
    if ([string]::IsNullOrWhiteSpace($Json)) { return @() }
    $parsed = $Json | ConvertFrom-Json
    if ($null -eq $parsed) { return @() }
    @($parsed)
}

# 弹窗有效自动关闭秒数：显式 popupTimeoutSeconds>0 优先；否则重复型默认 60s；否则 0(永不自动关)。
function Get-PopupTimeoutSeconds {
    param($Reminder)
    $t = [int]$Reminder.popupTimeoutSeconds
    if ($t -gt 0) { return $t }
    if ([int]$Reminder.repeatMinutes -gt 0) { return 60 }
    0
}

# 无动作提醒走系统通知(Toast)时的横幅时长：Windows 只给 短(~5-7s)/长(~25s) 两档，做不到任意秒数。
# 用「有效自动关闭秒数」判定：>=20 秒 → 长通知；否则短。这是能给纯 toast 的唯一时长旋钮。
# 复用 Get-PopupTimeoutSeconds（秒数的唯一来源），而非直接读 popupTimeoutSeconds——口径与弹窗一致、不会漂移。
function Test-ReminderToastLong {
    param($Reminder)
    ([int](Get-PopupTimeoutSeconds $Reminder)) -ge 20
}

# 把用户填的时间规整成规范 HH:mm，接受单数小时（"9:00"→"09:00"）。规整失败（空/非法）原样返回。
# 存盘前归一：GUI 的时间框是自由文本，若存进 "9:00" 这类单数小时，下游严格两位小时的 ParseExact / 正则
# 会判定失败——主时间→提醒永不触发；repeatUntil→截止钟点被忽略、重复催到 20 次上限。归一从源头堵住。
function Format-TimeHHmm {
    param([string]$Text)
    $s = ([string]$Text).Trim()
    if ($s -eq '') { return '' }
    $d = [datetime]::MinValue
    if ([datetime]::TryParseExact($s, [string[]]@('H:mm','HH:mm'), [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$d)) {
        return $d.ToString('HH:mm')
    }
    $s
}

function Get-DefaultReminders {
    # 通用示例提醒（不含个人内容），首次使用请按需修改。
    @(
        New-Reminder @{ time='10:00'; days=@(1,2,3,4,5); message='起来活动一下、喝口水~（示例提醒）' }
        New-Reminder @{ time='12:30'; message='午休时间到（示例）' }
        New-Reminder @{ time='15:30'; days=@(1,2,3,4,5); message='吃口水果，补充点维生素（示例·可开语音播报）'; speak=$true }
        New-Reminder @{ time='18:00'; days=@(1,2,3,4,5); message='下班啦，收拾一下桌面（示例）' }
        New-Reminder @{ time='23:00'; message='该睡觉了（示例）' }
    )
}

# 通知身份（AUMID），进程声明与显示名注册、Toast 发送共用同一值。带 .v2：通知平台按 AUMID 缓存
# 「应用归属」显示名，旧 ID 的乱码回退名已被缓存锁死，换代重建。
function Get-AppAumid { 'rockbenben.startupHelper.v2' }

function Get-DefaultConfig {
    [pscustomobject]@{
        launchSteps  = Get-DefaultLaunchSteps
        reminders    = Get-DefaultReminders
        # startMinimized：手动打开也直接进托盘、不显主窗
        # startupDelaySeconds：开机自启时从被唤醒起「诚实固定」等待的秒数——唯一的主延时杠杆，可预测、GUI 直接调（0–600）。
        #   登录风暴（磁盘/CPU 抢占）才是开机启动慢/失败的主因，故用一个够大的固定缓冲最实在；机器慢就把这个数字调大。
        # startupWaitForReady：可选的就绪门控（等桌面/网络就绪，就绪即走）。默认关——它测的是「壳/网存不存在」，
        #   冷启动一两秒就过、并不反映机器是否闲下来，形同安慰剂；确有「必须等网络起来再跑」的需求才在配置里开。
        # 二者仅作用于开机自启路径（-Boot）；手动「重新运行」环境本就就绪，不等待、不延迟。
        settings     = [pscustomobject]@{ tickSeconds = 30; startMinimized = $false; startupWaitForReady = $false; startupDelaySeconds = 30 }
        actionGroups = @()
    }
}

function ConvertTo-LaunchSteps {
    param($Old)
    $steps = New-Object System.Collections.ArrayList
    $ss = $Old.specialSteps
    if ($ss -and $ss.muteBefore8) { [void]$steps.Add((New-LaunchStep 'volume' @{ label='8点前静音'; action='mute'; onlyBefore8=$true })) }
    foreach ($it in @($Old.launchItems)) {
        [void]$steps.Add((New-LaunchStep 'app' @{
            enabled=[bool]$it.enabled; label=[string]$it.name; target=[string]$it.target
            args=[string]$it.args; workDir=[string]$it.workDir; elevated=[bool]$it.elevated; delayMs=[int]$it.delayMs }))
    }
    if ($ss) {
        if ($ss.wechatAutoLogin)     { [void]$steps.Add((New-LaunchStep 'window' @{ label='微信登录'; action='sendkey'; process='Weixin'; sendKey='{ENTER}' })) }
        if ($ss.closeTIM)            { [void]$steps.Add((New-LaunchStep 'window' @{ label='关闭 TIM'; action='close'; process='TIM' })) }
        if ($ss.closeQQ)             { [void]$steps.Add((New-LaunchStep 'window' @{ label='关闭 QQ'; action='close'; process='QQ' })) }
        if ($ss.minimizeThunderbird) { [void]$steps.Add((New-LaunchStep 'window' @{ label='最小化 Thunderbird'; action='minimize'; process='thunderbird' })) }
        foreach ($c in @($ss.extraSendKeys)) { if ($c) { [void]$steps.Add((New-LaunchStep 'keys' @{ label=[string]$c; combo=[string]$c })) } }
    }
    ,$steps.ToArray()
}

function Write-Config {
    param($Config, [string]$Path)
    ($Config | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Read-Config {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return Get-DefaultConfig }
    try { $j = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return Get-DefaultConfig }
    if ($null -eq $j.launchSteps -and ($null -ne $j.launchItems -or $null -ne $j.specialSteps)) {
        $j | Add-Member -NotePropertyName launchSteps -NotePropertyValue (ConvertTo-LaunchSteps $j) -Force
        $j.PSObject.Properties.Remove('launchItems')
        $j.PSObject.Properties.Remove('specialSteps')
    }
    $def = Get-DefaultConfig
    foreach ($k in 'launchSteps','reminders','settings','actionGroups') {
        if ($null -eq $j.$k) { $j | Add-Member -NotePropertyName $k -NotePropertyValue $def.$k -Force }
    }
    # settings 子键补默认：老配置缺新键（如 startMinimized）时，UI 勾选框对其赋值要求属性已存在。
    foreach ($sp in $def.settings.PSObject.Properties) {
        if (-not $j.settings.PSObject.Properties[$sp.Name]) { $j.settings | Add-Member -NotePropertyName $sp.Name -NotePropertyValue $sp.Value -Force }
    }
    # days 特性之前写出的步骤/提醒没有 days 字段；缺失即「每天」。不补则 @($null).Count=1 会被
    # Build-LaunchPlan / Get-ReminderDecision 误判为「有星期限制且今天不匹配」→ 步骤全被跳过（启动清单
    # 什么都不启动）、提醒永不触发。在此统一补成空数组（=每天），与新建项一致。
    foreach ($s in @($j.launchSteps)) {
        if ($s -and -not $s.PSObject.Properties['days']) { $s | Add-Member -NotePropertyName days -NotePropertyValue @() -Force }
    }
    foreach ($r in @($j.reminders)) {
        if (-not $r) { continue }
        # 稳定 id：旧配置无 id 时补一个（计时器运行时状态按 id 做键，缺/空会与他项串状态）。
        if (-not $r.PSObject.Properties['id'] -or [string]::IsNullOrWhiteSpace([string]$r.id)) { $r | Add-Member -NotePropertyName id ([guid]::NewGuid().ToString()) -Force }
        if (-not $r.PSObject.Properties['days'])               { $r | Add-Member -NotePropertyName days -NotePropertyValue @() -Force }
        if (-not $r.PSObject.Properties['trigger'])            { $r | Add-Member -NotePropertyName trigger 'time' -Force }
        if (-not $r.PSObject.Properties['graceMinutes'])       { $r | Add-Member -NotePropertyName graceMinutes 5 -Force }
        if (-not $r.PSObject.Properties['delaySeconds'])       { $r | Add-Member -NotePropertyName delaySeconds 0 -Force }
        if (-not $r.PSObject.Properties['randomDelaySeconds']) { $r | Add-Member -NotePropertyName randomDelaySeconds 0 -Force }
        if (-not $r.PSObject.Properties['repeatMinutes'])      { $r | Add-Member -NotePropertyName repeatMinutes 0 -Force }
        if (-not $r.PSObject.Properties['repeatUntil'])        { $r | Add-Member -NotePropertyName repeatUntil '' -Force }
        if (-not $r.PSObject.Properties['recurType'])    { $r | Add-Member -NotePropertyName recurType 'daily' -Force }
        if (-not $r.PSObject.Properties['intervalDays']) { $r | Add-Member -NotePropertyName intervalDays 1 -Force }
        if (-not $r.PSObject.Properties['monthlyDay'])   { $r | Add-Member -NotePropertyName monthlyDay 1 -Force }
        if (-not $r.PSObject.Properties['anchorDate'])   { $r | Add-Member -NotePropertyName anchorDate '' -Force }
        if (-not $r.PSObject.Properties['popupTimeoutSeconds']) { $r | Add-Member -NotePropertyName popupTimeoutSeconds 0 -Force }
        if (-not $r.PSObject.Properties['startupHourMode']) { $r | Add-Member -NotePropertyName startupHourMode 'any' -Force }
        if (-not $r.PSObject.Properties['startupHour'])     { $r | Add-Member -NotePropertyName startupHour 9 -Force }
        # 旧配置补 0（不限）而非 10：老用户的「登录时」提醒一直是每次启动都弹，静默改成「开机 10 分钟后
        # 重开不弹」是无声的行为变更（且 startupHandled 置位后整个会话都不再判定）。新建提醒仍默认 10。
        if (-not $r.PSObject.Properties['startupWithinMinutes']) { $r | Add-Member -NotePropertyName startupWithinMinutes 0 -Force }
        if (-not $r.PSObject.Properties['silentGroupId']) { $r | Add-Member -NotePropertyName silentGroupId '' -Force }
        # 清除废弃字段 confirm：提醒的是/否早已只由 onYes 决定，旧配置里的 confirm 是死键，落盘时一并去掉。
        if ($r.PSObject.Properties['confirm']) { $r.PSObject.Properties.Remove('confirm') }
    }
    # 动作组：补稳定 id + 缺字段（对照 New-ActionGroup 默认）。
    foreach ($g in @($j.actionGroups)) {
        if (-not $g) { continue }
        if (-not $g.PSObject.Properties['id'] -or [string]::IsNullOrWhiteSpace([string]$g.id)) { $g | Add-Member -NotePropertyName id ([guid]::NewGuid().ToString()) -Force }
        $agDef = New-ActionGroup
        foreach ($p in $agDef.PSObject.Properties) {
            if ($p.Name -eq 'id') { continue }
            if (-not $g.PSObject.Properties[$p.Name]) { $g | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value -Force }
        }
    }
    $j
}

function New-ReminderState {
    @{ lastFiredDate=''; startupHandled=$false; pendingFireAt=$null; nextRepeatAt=$null; repeatCount=0; snoozeUntil=$null }
}

# 用户「稍后」N 分钟：钉一次性 snoozeUntil（显式请求，独立于周期重复/repeatUntil/周期日；故由 Get-ReminderDecision
# 在周期门之前判定），并清掉进行中的周期重复待发。N<1 视作默认 10 分钟。
# 保留 repeatCount：snooze 后若续上重复，仍受 MAX_REPEATS 安全帽约束，不让反复 snooze 绕过封顶。
function Set-ReminderSnooze {
    param($State, [datetime]$Now, [int]$Minutes)
    if ($Minutes -lt 1) { $Minutes = 10 }
    $State.nextRepeatAt = $null
    $State.snoozeUntil = $Now.AddMinutes($Minutes)
    return $State
}

# 今天是否落在提醒周期上。daily=星期过滤(空=每天)；everyNDays=从 anchorDate 取模(防漂移)；monthly=每月第N天(夹月末)。
function Test-RecurrenceDueToday {
    param($Reminder, [datetime]$Today)
    switch ([string]$Reminder.recurType) {
        'everyNDays' {
            $n = [int]$Reminder.intervalDays; if ($n -lt 1) { $n = 1 }
            $a = [string]$Reminder.anchorDate
            if ([string]::IsNullOrWhiteSpace($a)) { return $true }
            try { $anchor = [datetime]::ParseExact($a, 'yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture).Date } catch { return $true }
            if ($Today.Date -lt $anchor) { return $false }
            return ((($Today.Date - $anchor).Days % $n) -eq 0)
        }
        'monthly' {
            $d = [int]$Reminder.monthlyDay; if ($d -lt 1) { $d = 1 }; if ($d -gt 31) { $d = 31 }
            $eff = [Math]::Min($d, [System.DateTime]::DaysInMonth($Today.Year, $Today.Month))
            return ($Today.Day -eq $eff)
        }
        default {
            $days = @($Reminder.days)
            if ($days.Count -eq 0) { return $true }
            $iso = [int]$Today.DayOfWeek; if ($iso -eq 0) { $iso = 7 }
            return ($days -contains $iso)
        }
    }
}

# 登录时刻小时是否满足提醒的 startup 限制。before=登录小时<阈值; after=登录小时>=阈值; 其它=不限。
function Test-StartupHourOk {
    param($Reminder, [datetime]$StartTime)
    $mode = [string]$Reminder.startupHourMode
    if ($mode -ne 'before' -and $mode -ne 'after') { return $true }
    $h = [int]$Reminder.startupHour
    $loginHour = $StartTime.Hour
    if ($mode -eq 'before') { return ($loginHour -lt $h) }
    return ($loginHour -ge $h)
}

# 系统开机至今的分钟数。Stopwatch 的 QPC 计数从开机起算，纯 .NET、不经 WMI/P-Invoke。
function Get-SystemUptimeMinutes {
    [int]([double][System.Diagnostics.Stopwatch]::GetTimestamp() / [System.Diagnostics.Stopwatch]::Frequency / 60)
}

# 触发判定纯函数。返回 @{ action='none'|'arm'|'fire'; base=<datetime|$null>; state }。
# 不掷随机、不弹窗：'arm' 交给 GUI 据 base+延迟(含随机)算 pendingFireAt 并回写 state。
# $UptimeMinutes：程序启动那一刻的系统开机分钟数（GUI 传入；-1=未知则不做开机时段门控，保持纯函数可测）。
function Get-ReminderDecision {
    param($Reminder, [datetime]$Now, [datetime]$StartTime, $State, [int]$UptimeMinutes = -1)
    $st = $State
    $mk = { param($a,$b) [pscustomobject]@{ action=$a; base=$b; state=$st } }

    if (-not $Reminder.enabled) { return (& $mk 'none' $null) }

    # 稍后(snooze)：一次性、显式请求，优先于周期门——跨午夜落到非周期日也照发一次，到点即清。
    if ($st.snoozeUntil) {
        if ($Now -ge $st.snoozeUntil) { $st.snoozeUntil = $null; return (& $mk 'fire' $null) }
        return (& $mk 'none' $null)
    }

    # 周期过滤（每天/每N天/每月某日）；不在周期内则清掉任何待发/重复
    if (-not (Test-RecurrenceDueToday $Reminder $Now)) {
        $st.pendingFireAt = $null; $st.nextRepeatAt = $null
        return (& $mk 'none' $null)
    }

    $today = $Now.ToString('yyyy-MM-dd')

    # 1) 重复到点优先
    if ($st.nextRepeatAt) {
        if ($Now -ge $st.nextRepeatAt) { $st.nextRepeatAt = $null; return (& $mk 'fire' $null) }
        return (& $mk 'none' $null)
    }

    # 2) 已 arm，等延迟到点
    if ($st.pendingFireAt) {
        if ($Now -ge $st.pendingFireAt) {
            $st.pendingFireAt = $null; $st.lastFiredDate = $today
            if ($Reminder.trigger -eq 'startup') { $st.startupHandled = $true }
            return (& $mk 'fire' $null)
        }
        return (& $mk 'none' $null)
    }

    # 3) 首发判定
    if ($Reminder.trigger -eq 'startup') {
        # 「登录时」只认真正的开机时段：开机超过 startupWithinMinutes 分钟后再启动本程序（白天手动重开等）
        # 不算登录、不弹。0=不限（每次启动都算）；$UptimeMinutes<0（未传）不门控。
        $limit = [int]$Reminder.startupWithinMinutes
        if ($limit -gt 0 -and $UptimeMinutes -ge 0 -and $UptimeMinutes -gt $limit) {
            $st.startupHandled = $true   # 本次运行不再反复判定
            return (& $mk 'none' $null)
        }
        if (-not $st.startupHandled -and $Now -ge $StartTime -and (Test-StartupHourOk $Reminder $StartTime)) { return (& $mk 'arm' $StartTime) }
        return (& $mk 'none' $null)
    }
    if ($st.lastFiredDate -eq $today) { return (& $mk 'none' $null) }
    # time 可能来自手改 json（如 "9:00" 单位小时）：ParseExact 抛异常会在计时器 tick 里反复弹崩溃框、
    # 并挡住后面所有提醒的评估。解析失败按 none 处理（该条不触发，其余提醒不受牵连）。
    try { $base = [datetime]::ParseExact("$today $($Reminder.time)", 'yyyy-MM-dd HH:mm', [System.Globalization.CultureInfo]::InvariantCulture) }
    catch { return (& $mk 'none' $null) }
    $grace = [int]$Reminder.graceMinutes; if ($grace -lt 0) { $grace = 0 }
    # 取整到分钟比较：$Now 带秒/毫秒，否则 grace=0 永远不等于整分的 $base → 永不触发。
    $nowMin = $Now.Date.AddHours($Now.Hour).AddMinutes($Now.Minute)
    if ($nowMin -ge $base -and $nowMin -le $base.AddMinutes($grace)) { return (& $mk 'arm' $base) }
    return (& $mk 'none' $null)
}

function Get-ReminderMaxRepeats { 20 }

# 弹窗后推进周期重复状态。确认(yes/no/ok)=停；未确认('')按 repeatMinutes 排下次，
# 受 repeatUntil 截止钟点与 MAX_REPEATS 安全上限约束。
# 注：「稍后」由 Set-ReminderSnooze 写 snoozeUntil 单独处理，不经此函数。
function Update-ReminderAfterFire {
    param($Reminder, [datetime]$Now, [string]$Result, $State)
    $st = $State
    if ($Result -in @('yes','no','ok')) { $st.nextRepeatAt=$null; $st.repeatCount=0; return $st }

    $rep = [int]$Reminder.repeatMinutes
    if ($rep -le 0) { $st.nextRepeatAt=$null; return $st }

    $count = [int]$st.repeatCount + 1
    if ($count -ge (Get-ReminderMaxRepeats)) { $st.nextRepeatAt=$null; $st.repeatCount=0; return $st }

    $next = $Now.AddMinutes($rep)
    if ($Reminder.repeatUntil -match '^([01]\d|2[0-3]):[0-5]\d$') {
        $until = [datetime]::ParseExact("$($Now.ToString('yyyy-MM-dd')) $($Reminder.repeatUntil)", 'yyyy-MM-dd HH:mm', [System.Globalization.CultureInfo]::InvariantCulture)
        if ($next -gt $until) { $st.nextRepeatAt=$null; $st.repeatCount=0; return $st }
    }
    $st.repeatCount = $count
    $st.nextRepeatAt = $next
    return $st
}

function ConvertFrom-KeyCombo {
    param([string]$Combo)
    $mods = @(); $key = $null
    foreach ($p in ($Combo -split '\+')) {
        $t = $p.Trim()
        switch ($t.ToLower()) {
            'win'     { $mods += 'Win' }
            'ctrl'    { $mods += 'Ctrl' }
            'control' { $mods += 'Ctrl' }
            'alt'     { $mods += 'Alt' }
            'shift'   { $mods += 'Shift' }
            ''        { }
            default   { $key = $t }
        }
    }
    [pscustomobject]@{ Modifiers = $mods; Key = $key; UseWin = ($mods -contains 'Win') }
}

# 解析「稍后」时长文本 -> 分钟。纯数字=分钟；'1h20m'/'2h'/'45m' 解析时+分。
# 非法/空/<1/超过 7 天 -> $null（调用方回退默认值）。
function ConvertFrom-DurationText {
    param([string]$Text)
    if ($null -eq $Text) { return $null }
    $t = ($Text -replace '\s','').ToLower()
    if ($t -eq '') { return $null }
    # 数字过大时 [int] 强转会抛 OverflowException（如 snooze 框输入 9999999999）；
    # 按契约应回退 $null 而非抛异常/崩溃，故整体 try 包住转换。
    try {
        if ($t -match '^\d+$') {
            $mins = [int64]$t
        } elseif ($t -match '^(\d+h)?(\d+m)?$') {
            $h = 0; $m = 0
            if ($t -match '(\d+)h') { $h = [int64]$matches[1] }
            if ($t -match '(\d+)m') { $m = [int64]$matches[1] }
            $mins = $h * 60 + $m
        } else {
            return $null
        }
    } catch { return $null }
    if ($mins -lt 1 -or $mins -gt 10080) { return $null }
    [int]$mins
}

# 目标路径是否就是开机助手自身（防开机自启动循环）。规范化后大小写不敏感比较。
function Test-IsSelfTarget {
    param([string]$Target, [string[]]$SelfPaths)
    if ([string]::IsNullOrWhiteSpace($Target)) { return $false }
    try { $tf = [System.IO.Path]::GetFullPath($Target) } catch { return $false }
    foreach ($sp in @($SelfPaths)) {
        if ([string]::IsNullOrWhiteSpace($sp)) { continue }
        try { $sf = [System.IO.Path]::GetFullPath($sp) } catch { continue }
        if ([string]::Equals($tf, $sf, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    $false
}

# 解析结果 → SendKeys 字符串（非 Win 组合用）。关键：SendKeys 把【大写字母】当作
# 「Shift+该键」，所以 'Alt+K' 直接发 '%K' 会变成 Alt+Shift+K（用户的 Alt+K 因此没生效）。
# Shift 已由 '+' 前缀显式表达，故单个字母键一律转小写，避免多按一个 Shift。
function ConvertTo-SendKeysString {
    param($Parsed)
    $prefix = ''
    if ($Parsed.Modifiers -contains 'Ctrl')  { $prefix += '^' }
    if ($Parsed.Modifiers -contains 'Alt')   { $prefix += '%' }
    if ($Parsed.Modifiers -contains 'Shift') { $prefix += '+' }
    $k = [string]$Parsed.Key
    # 命名特殊键 → SendKeys 花括号代码：裸词（如 'Enter'）会被 SendKeys 当成逐字符序列，
    # 修饰键只作用到第一个字符——'^Enter' 实际按下 Ctrl+E 再把 "nter" 打进焦点窗口。
    $named = @{ enter='{ENTER}'; return='{ENTER}'; tab='{TAB}'; esc='{ESC}'; escape='{ESC}'; space=' '
                backspace='{BACKSPACE}'; bs='{BACKSPACE}'; del='{DEL}'; delete='{DEL}'; ins='{INS}'; insert='{INS}'
                home='{HOME}'; end='{END}'; pgup='{PGUP}'; pageup='{PGUP}'; pgdn='{PGDN}'; pagedown='{PGDN}'
                up='{UP}'; down='{DOWN}'; left='{LEFT}'; right='{RIGHT}'; printscreen='{PRTSC}'; prtsc='{PRTSC}' }
    if ($k.Length -eq 1 -and $k -match '[A-Za-z]') { $k = $k.ToLower() }
    # 功能键 F1..F12 在 SendKeys 里必须带花括号({F4})，否则 'F4' 被当成字面字符 'F''4'。
    # 用户在「发送按键」里直接填 Alt+F4 很自然，故未加花括号时自动补上（已带花括号的原样保留）。
    elseif ($k -match '^[Ff](1[0-2]|[1-9])$') { $k = '{' + $k.ToUpper() + '}' }
    elseif ($named.ContainsKey($k.ToLower())) { $k = $named[$k.ToLower()] }
    # 多字符且不认识、也没带花括号 → 发出去会变成「修饰键+首字母 + 打出剩余字面文本」，
    # 返回 $null 让调用方拒发并告警（与 Win 路径的键名校验对齐，别把垃圾文字注入焦点窗口）。
    elseif ($k.Length -gt 1 -and $k -notmatch '^\{') { return $null }
    "$prefix$k"
}

# 窗口步骤「发送按键」内容的宽容解析：带花括号/非组合形态的原样当 SendKeys 序列（如 '{ENTER}'、'hello{TAB}'）；
# 'Enter'、'Ctrl+Enter' 这类组合写法自动转成 SendKeys（用户不必懂花括号语法）；转不出来退回原样（当字面文本）。
function ConvertTo-SendKeysSequence {
    param([string]$Raw)
    if ([string]::IsNullOrEmpty($Raw)) { return $Raw }
    if ($Raw -match '[{}]' -or $Raw -notmatch '^[A-Za-z0-9]+(\+[A-Za-z0-9]+)*$') { return $Raw }
    $p = ConvertFrom-KeyCombo $Raw
    if ($p.UseWin -or [string]::IsNullOrEmpty([string]$p.Key)) { return $Raw }   # SendKeys 不支持 Win 键
    $s = ConvertTo-SendKeysString $p
    if ($s) { $s } else { $Raw }
}

# 时间条件的小时阈值：缺失/越界一律回退 8（兼容旧配置里只有 onlyBefore8、没有 beforeHour 的步骤）。
function Get-BeforeHour {
    param($Step)
    $h = [int]$Step.beforeHour
    if ($h -lt 1 -or $h -gt 23) { 8 } else { $h }
}

# 星期集合 → 文案：空或全 7 天=「每天」，否则列出（一二三四五六日）。提醒与启动步骤共用。
function Get-DaysLabel {
    param($Days)
    $d = @($Days)
    if ($d.Count -eq 0 -or $d.Count -eq 7) { '每天' }
    else { ($d | Sort-Object | ForEach-Object { '一二三四五六日'[$_-1] }) -join '' }
}

# 步骤时间条件（仅星期 / 仅 N 点前）是否满足。顶层启动清单与动作组内步骤统一遵守——
# 编辑器里所有步骤都能设条件，不满足即跳过。缺失字段按「无限制」处理（旧配置/手写 json 兼容）。
function Test-StepCondition {
    param($Step, [int]$CurrentHour, [int]$CurrentIsoDay = 0)
    if ($CurrentIsoDay -le 0) { $CurrentIsoDay = [int](Get-Date).DayOfWeek; if ($CurrentIsoDay -eq 0) { $CurrentIsoDay = 7 } }   # .NET 周日=0 → ISO 7
    if ($Step.onlyBefore8 -and $CurrentHour -ge (Get-BeforeHour $Step)) { return $false }
    $days = @(@($Step.days) | Where-Object { $null -ne $_ })   # 缺 days 时 @($null) 会误判成「有限制」→ 先滤掉 null
    if ($days.Count -gt 0 -and ($days -notcontains $CurrentIsoDay)) { return $false }
    $true
}

function Build-LaunchPlan {
    param($Config, [int]$CurrentHour, [int]$CurrentIsoDay = 0)
    if ($CurrentIsoDay -le 0) { $CurrentIsoDay = [int](Get-Date).DayOfWeek; if ($CurrentIsoDay -eq 0) { $CurrentIsoDay = 7 } }   # .NET 周日=0 → ISO 7
    $plan = New-Object System.Collections.ArrayList
    foreach ($s in @($Config.launchSteps)) {
        if (-not $s.enabled) { continue }
        if (-not (Test-StepCondition $s $CurrentHour $CurrentIsoDay)) { continue }
        [void]$plan.Add($s)
    }
    ,$plan.ToArray()
}

function Get-StepKindLabel {
    param([string]$Kind)
    switch ($Kind) { 'app' {'启动程序'} 'keys' {'发送按键'} 'volume' {'音量'} 'window' {'窗口动作'} 'system' {'系统命令'} 'group' {'动作组'} 'message' {'消息'} 'delay' {'延时'} default {$Kind} }
}

function Get-SystemCommandMap {
    [ordered]@{
        showDesktop='显示桌面'; lockScreen='锁屏（回来需输密码）'; emptyRecycleBin='清空回收站'; openSettings='打开 Windows 设置'; screenshot='截图（框选屏幕区域）'
        clearClipboard='清空剪贴板'; taskManager='打开任务管理器'; monitorOff='息屏（只关屏幕，动鼠标即亮）'
        sleep='睡眠（低功耗待机，秒醒）'; hibernate='休眠（存盘断电，开机恢复现场）'; signOut='注销（退出登录，需确认）'; restart='重启（需确认）'; shutdown='关机（需确认）'
    }
}

function Get-SystemCommandLabel {
    param([string]$Id)
    $m = Get-SystemCommandMap
    if ($m.Contains($Id)) { $m[$Id] } else { $Id }
}

function Get-StepSummary {
    param($Step)
    $base = switch ($Step.kind) {
        'app'    { if ($Step.label) { [string]$Step.label } else { [string]$Step.target } }
        'keys'   { "发送 $($Step.combo)" }
        'volume' { switch ($Step.action) { 'mute' {'静音'} 'unmute' {'取消静音'} 'set' {"设音量 $([int]$Step.level)%"} default {[string]$Step.action} } }
        'window' { "$(switch ($Step.action) { 'close' {'关闭窗口'} 'minimize' {'最小化窗口'} 'maximize' {'最大化窗口'} 'activate' {'带到最前面'} 'sendkey' {'置前并发送按键'} default {[string]$Step.action} }) $($Step.process)" }
        'system' { Get-SystemCommandLabel ([string]$Step.command) }
        'group'  { "运行动作组：$(if ($Step.label) { [string]$Step.label } elseif ($Step.groupId) { [string]$Step.groupId } else { '(未指定)' })" }
        'delay'  { $ms=[int]$Step.delayMs; if ($ms % 1000 -eq 0) { "延时 $($ms/1000) 秒" } else { "延时 $ms 毫秒" } }
        'message' { ([string]$Step.message -replace "`r?`n",' ') }
        default  { [string]$Step.kind }
    }
    $s = $base
    $dc = @(@($Step.days) | Where-Object { $null -ne $_ })   # 手写 json 缺 days 时 @($null) 会生成假的星期后缀
    if ($dc.Count -gt 0 -and $dc.Count -lt 7) { $s += "（$(Get-DaysLabel $dc)）" }
    if ($Step.onlyBefore8) { $s += "（仅$(Get-BeforeHour $Step)点前）" }
    $s
}
