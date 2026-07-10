# 开机助手（图形界面）。登录自启由任务计划以 -Run 调用；双击 .bat 仅打开编辑窗口。
param([switch]$Run)

Set-Location -LiteralPath $PSScriptRoot

# 兜底可写的 %TEMP%：某些启动器/快捷工具（如 Lucy）会用被清空/无效的环境拉起本进程，令 TEMP 指向不存在
# 或不可写的目录。而后面多处 Add-Type 要【运行时编译 C#】（ShRow 行模型、SH.Native 发键、DPI 声明），
# 编译需可写临时目录；TEMP 无效则编译失败、被各处 try/catch 吞掉 —— 尤其 ShRow 没定义会让主窗口
# 构建时抛「找不到类型 [ShRow]」、整个应用起不来。故开局先把 TEMP/TMP 落到一个确实可写的目录。
foreach ($c in @($env:TEMP, "$env:LOCALAPPDATA\Temp", "$env:USERPROFILE\AppData\Local\Temp", "$env:WINDIR\Temp", (Join-Path $PSScriptRoot '.temp'))) {
    if (-not $c) { continue }
    try {
        if (-not (Test-Path -LiteralPath $c -PathType Container)) { New-Item -ItemType Directory -Path $c -Force -ErrorAction Stop | Out-Null }
        $probe = Join-Path $c "sh_probe_$PID.tmp"
        [System.IO.File]::WriteAllText($probe, 'x'); Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        $env:TEMP = $c; $env:TMP = $c; break   # 第一个可写的即用；正常情况下就是原 TEMP，不改动
    } catch {}
}

# 高分辨率：尽早（建任何窗口前）声明「系统级 DPI 感知」，让计划任务/conhost/双击 任何启动路径
# 都在当前缩放下清晰渲染，而不是被 Windows 位图拉伸发虚。逐显示器(PerMonitor)在 .NET Framework
# WinForms 上不可靠（不会自动重排），故取系统级。宿主已声明时本调用是无害 no-op。
try {
    Add-Type -Namespace SHDpi -Name Awareness -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr value);
[System.Runtime.InteropServices.DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@ -ErrorAction Stop
    $done = $false
    try { $done = [SHDpi.Awareness]::SetProcessDpiAwarenessContext([System.IntPtr](-2)) } catch {}   # DPI_AWARENESS_CONTEXT_SYSTEM_AWARE
    if (-not $done) { try { [void][SHDpi.Awareness]::SetProcessDpiAwareness(1) }                      # PROCESS_SYSTEM_DPI_AWARE
                      catch { try { [void][SHDpi.Awareness]::SetProcessDPIAware() } catch {} } }
} catch {}

Add-Type -AssemblyName System.Windows.Forms   # 仅用其 NotifyIcon 做托盘
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase   # WPF 界面
[System.Windows.Forms.Application]::EnableVisualStyles()
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# 全局异常兜底：未捕获异常不再弹原生「应用程序中发生了未经处理的异常」框，
# 而是写日志 + 友好提示；UI 线程消息循环里抛的异常被接住后，应用可继续运行。
$script:ErrLogPath = Join-Path $PSScriptRoot '开机助手-错误.log'
function Show-CrashDialog {
    param($Ex)
    try {
        $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        $detail = if ($Ex -is [System.Exception]) { "$($Ex.GetType().FullName): $($Ex.Message)`r`n$($Ex.StackTrace)" } else { [string]$Ex }
        Add-Content -LiteralPath $script:ErrLogPath -Value "[$ts] $detail`r`n" -Encoding UTF8
    } catch {}
    try {
        $m = if ($Ex -is [System.Exception]) { $Ex.Message } else { [string]$Ex }
        [System.Windows.MessageBox]::Show("开机助手遇到一个错误（已记录到日志，通常可继续使用）：`r`n`r`n$m`r`n`r`n日志：$script:ErrLogPath",
            '开机助手 · 出错了', [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning) | Out-Null
    } catch {}
}
# UI 线程异常由 WPF 的 DispatcherUnhandledException 接（见下方 $app）；这里兜非 UI 线程/终结器等的漏网异常。
# （原 WinForms ThreadException 钩子在 WPF 消息循环下不会触发，已移除。）
[System.AppDomain]::CurrentDomain.add_UnhandledException({ param($s, $e) Show-CrashDialog $e.ExceptionObject })

# 通知身份（AUMID）。带 .v2 后缀：Windows 通知平台按 AUMID 缓存「应用归属」显示名——旧 ID 在注册
# DisplayName 之前发过通知，乱码回退名已被缓存锁死（注册表值正确也不刷新）。换新 ID 让归属重建。
$aumid = 'rockbenben.startupHelper.v2'
try {
    Add-Type -Namespace Native -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
public static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);
'@
} catch {}

# 先注册显示名/图标、再声明进程 AUMID、之后才可能发通知——顺序保证首条通知就用正确归属。
try {
    $aumidKey = "HKCU:\Software\Classes\AppUserModelId\$aumid"
    if (-not (Test-Path -LiteralPath $aumidKey)) { New-Item -Path $aumidKey -Force | Out-Null }
    Set-ItemProperty -LiteralPath $aumidKey -Name 'DisplayName' -Value '开机助手' -Force
    $aumidIco = Join-Path $PSScriptRoot 'assets\logo.ico'
    if (Test-Path -LiteralPath $aumidIco) { Set-ItemProperty -LiteralPath $aumidKey -Name 'IconUri' -Value $aumidIco -Force }
} catch {}
try { [Native.Shell]::SetCurrentProcessExplicitAppUserModelID($aumid) | Out-Null } catch {}

$script:SelfPath = Join-Path $PSScriptRoot 'startup-helper.ps1'
$script:AppRoot  = $PSScriptRoot   # 后台 runspace 跑启动序列时据此加载 lib
$script:LaunchSelfPaths = @($script:SelfPath, (Join-Path $PSScriptRoot '开机助手-双击运行.bat'))   # 自启动守卫：清单跑到自身则跳过
. (Join-Path $PSScriptRoot 'lib\StartupHelper.Core.ps1')
. (Join-Path $PSScriptRoot 'lib\StartupHelper.Win32.ps1')
. (Join-Path $PSScriptRoot 'lib\StartupHelper.SystemStartup.ps1')
. (Join-Path $PSScriptRoot 'lib\StartupHelper.Actions.ps1')
. (Join-Path $PSScriptRoot 'lib\StartupHelper.WpfGui.ps1')
. (Join-Path $PSScriptRoot 'lib\StartupHelper.WpfDialogs.ps1')
. (Join-Path $PSScriptRoot 'lib\StartupHelper.WpfSteps.ps1')   # 步骤对话框 + 动作组编辑器（覆盖占位实现）

# 单实例：已运行则置信号让旧实例显示窗口，自己退出
$createdNew = $false
$mutex = New-Object System.Threading.Mutex($true, 'Global\rockbenben.startupHelper.mutex', [ref]$createdNew)
$showEvt = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::AutoReset, 'Global\rockbenben.startupHelper.show')
if (-not $createdNew) {
    # 可能是「以管理员重开」的旧实例正在退出，等它释放互斥；否则确有别的实例 -> 唤起它
    $got = $false
    try { $got = $mutex.WaitOne(1200) } catch [System.Threading.AbandonedMutexException] { $got = $true }
    if (-not $got) { [void]$showEvt.Set(); return }
}

$cfgPath = Join-Path $PSScriptRoot '开机助手-设置.json'
if (-not (Test-Path -LiteralPath $cfgPath)) {
    # 首次运行：优先用随仓库附带的示例配置（含示例启动/提醒 + 动作组模板）开局；缺失则生成通用默认。
    $examplePath = Join-Path $PSScriptRoot '开机助手-设置.example.json'
    if (Test-Path -LiteralPath $examplePath) { Copy-Item -LiteralPath $examplePath -Destination $cfgPath -Force }
    else { Write-Config (Get-DefaultConfig) $cfgPath }
}
# 旧格式（launchItems/specialSteps）由 Read-Config 自动迁移；若确为旧格式，把升级后的配置回写一次
$wasOld = (Get-Content -LiteralPath $cfgPath -Raw -Encoding UTF8 -ErrorAction SilentlyContinue) -match '"launchItems"'
$script:Config = Read-Config $cfgPath
$script:CfgPath = $cfgPath
if ($wasOld) { Write-Config $script:Config $cfgPath }

$tray = $null
try {
    $app = New-Object System.Windows.Application
    $app.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown   # 关窗=隐到托盘；退出仅经托盘「退出」
    $app.add_DispatcherUnhandledException({ param($s, $e) Show-CrashDialog $e.Exception; $e.Handled = $true })
    # 后台 runspace 轮询用 DispatcherTimer（WinForms.Timer 在 WPF 消息循环下不可靠）
    $script:MakeAsyncTimer = { $t = New-Object System.Windows.Threading.DispatcherTimer; $t.Interval = [TimeSpan]::FromMilliseconds(400); $t }

    $win = Show-WpfMainWindow $script:Config
    $tray = Add-WpfTray $win $script:Config
    $timer = Start-WpfReminderTimer $win $script:Config

    # 跨实例「显示窗口」信号：每秒检查一次
    $showTimer = New-Object System.Windows.Threading.DispatcherTimer
    $showTimer.Interval = [TimeSpan]::FromSeconds(1)
    $showTimer.Add_Tick({ if ($showEvt.WaitOne(0)) { Show-MainWin $win } }.GetNewClosure())
    $showTimer.Start()

    if ($Run) {
        # 自启路径：不显窗、只入托盘，延迟到消息循环起来后异步跑启动清单（含 Sleep/前台等待，不冻结 UI）。
        $win.ShowInTaskbar = $false
        $boot = New-Object System.Windows.Threading.DispatcherTimer
        $boot.Interval = [TimeSpan]::FromMilliseconds(800)
        $boot.Add_Tick({ $boot.Stop(); Invoke-LaunchSequenceAsync $script:Config -Boot }.GetNewClosure())
        $boot.Start()
    } elseif ([bool]$script:Config.settings.startMinimized) {
        # 「启动时最小化到托盘」：手动打开也只入托盘，不显主窗（双击托盘图标随时打开）。静默入托盘，不弹气泡。
        $win.ShowInTaskbar = $false
    } else {
        $win.Show()
    }
    $app.Run()
} catch {
    # 建窗/初始化阶段抛的异常走这里：记日志 + 提示后优雅退出，不弹原生异常框。
    Show-CrashDialog $_.Exception
} finally {
    if ($tray) { $tray.Visible = $false }
    try { $mutex.ReleaseMutex() } catch {}
    $mutex.Dispose(); $showEvt.Dispose()
}
