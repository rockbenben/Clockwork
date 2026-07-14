# Clockwork.Win32.ps1 —— Win32 P/Invoke 封装
Add-Type -AssemblyName System.Windows.Forms

# 把 C# 编译（SH.Native/SH.Audio，各 spawn 一次 csc、约 200ms）推迟到真正用到时（启动序列），
# 让"仅打开编辑窗口"的场景免去这段编译、更快打开。幂等：已编译则直接返回。
# 任何 [SH.Native]/[SH.Audio] 使用前必须先调用一次（已在 Invoke-LaunchSequence 开头调用）。
function Initialize-Win32Types {
if (-not ('SH.Native' -as [type])) {
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
namespace SH {
  public static class Native {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public int type; public InputUnion U; }
    static INPUT MakeKey(ushort vk, bool up) {
      var inp = new INPUT(); inp.type = 1; // INPUT_KEYBOARD
      inp.U.ki.wVk = vk; inp.U.ki.dwFlags = up ? 2u : 0u; // KEYEVENTF_KEYUP
      return inp;
    }
    // 官方推荐路径：整个组合（修饰键按下→主键按下/抬起→修饰键逆序抬起）一次 SendInput 原子注入，
    // 不会与用户真实击键交错；返回实际注入的事件数（0 = 被 UIPI/安全桌面拒绝，调用方可如实报失败）。
    public static uint SendCombo(ushort[] mods, ushort vk) {
      var list = new List<INPUT>();
      foreach (var m in mods) list.Add(MakeKey(m, false));
      list.Add(MakeKey(vk, false)); list.Add(MakeKey(vk, true));
      for (int i = mods.Length - 1; i >= 0; i--) list.Add(MakeKey(mods[i], true));
      var arr = list.ToArray();
      return SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }
    // 部分注入的善后：给每个键补发抬起事件，防止修饰键（Win/Ctrl/…）被卡在按下态。
    public static void ReleaseKeys(ushort[] vks) {
      var list = new List<INPUT>();
      foreach (var k in vks) list.Add(MakeKey(k, true));
      var arr = list.ToArray();
      SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
    }
    delegate bool EnumProc(IntPtr h, IntPtr p);
    public static IntPtr[] WindowsForProcess(string procName) {
      var pids = new HashSet<uint>();
      foreach (var pr in Process.GetProcessesByName(procName)) { pids.Add((uint)pr.Id); }
      var list = new List<IntPtr>();
      EnumWindows((h, p) => {
        if (!IsWindowVisible(h)) return true;
        uint pid; GetWindowThreadProcessId(h, out pid);
        if (pids.Contains(pid)) list.Add(h);
        return true;
      }, IntPtr.Zero);
      return list.ToArray();
    }
  }
}
'@
}

if (-not ('SH.Audio' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
namespace SH {
  [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IMMDeviceEnumerator {
    int f0();  // EnumAudioEndpoints
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
  }
  [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IMMDevice {
    [PreserveSig] int Activate([In] ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
  }
  [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IAudioEndpointVolume {
    int f0(); int f1(); int f2();                 // Register/Unregister/GetChannelCount
    int f3();                                     // SetMasterVolumeLevel
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, [In] ref Guid ctx);
    int f5(); int f6(); int f7(); int f8(); int f9(); int f10();  // Get*/SetChannel*/GetChannel*
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, [In] ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
  }
  [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
  public static class Audio {
    // 无可用输出设备（RDP 未重定向音频 / 无声卡 / 输出全禁用）时 GetDefaultAudioEndpoint 返回 E_NOTFOUND、
    // dev 为 null——必须查 HRESULT 并判空，否则解引用 null 抛 NRE；音量步骤崩了还会中止整个动作组（后续步骤不跑）。
    private static IAudioEndpointVolume Endpoint() {
      var en = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
      IMMDevice dev;
      if (en.GetDefaultAudioEndpoint(0, 1, out dev) < 0 || dev == null) return null;   // eRender, eMultimedia
      Guid iid = typeof(IAudioEndpointVolume).GUID;
      object o;
      if (dev.Activate(ref iid, 1, IntPtr.Zero, out o) < 0 || o == null) return null;  // CLSCTX_INPROC_SERVER
      return (IAudioEndpointVolume)o;
    }
    public static void SetVolume(float level) {
      var ep = Endpoint(); if (ep == null) return;   // 无音频设备 → 静默跳过，不崩、不中止动作组
      Guid ctx = Guid.Empty;
      int hr = ep.SetMasterVolumeLevelScalar(level, ref ctx);
      if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }
    public static void SetMute(bool mute) {
      var ep = Endpoint(); if (ep == null) return;   // 无音频设备 → 静默跳过
      Guid ctx = Guid.Empty;
      int hr = ep.SetMute(mute, ref ctx);
      if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }
  }
}
'@
}
}

# 运行时编译 SH.Native/SH.Audio 是否可用。受限令牌/无效 TEMP 下（如经沙箱/受限令牌的启动器 Lucy 打开）
# csc 编译会失败（报「客户端没有所需的特权」）。用它把「原生功能」的失败变成一句清楚的提示，而非晦涩报错。
$script:Win32Unavailable = $false
$script:Win32Error = $null   # 首次编译失败的真实异常（csc 报的原话）——诊断时比笼统提示有用得多，别再丢掉
$script:NativeRestrictedMsg = '启动环境受限，此原生功能不可用（发送按键/窗口动作/激活/音量等需正常启动：双击 bat、托盘或计划任务，勿用沙箱/受限启动器如 Lucy）。'
function Confirm-Win32Available {
    if ($script:Win32Unavailable) { return $false }
    try { Initialize-Win32Types; return $true } catch { $script:Win32Unavailable = $true; $script:Win32Error = $_.Exception.Message; return $false }
}
# 对用户展示的原生不可用提示：带上底层真实原因（如「客户端没有所需的特权」=受限令牌 / TEMP 不可写 / csc 缺失）。
function Get-NativeRestrictedMsg {
    if ($script:Win32Error) { "$script:NativeRestrictedMsg`n底层原因：$script:Win32Error" } else { $script:NativeRestrictedMsg }
}

$script:WM_CLOSE = 0x0010
$script:SW_MINIMIZE = 6
$script:SW_MAXIMIZE = 3
$script:SW_RESTORE = 9

function Get-AppWindowHandles { param([string]$ProcessName) ,([SH.Native]::WindowsForProcess((ConvertTo-ProcessName $ProcessName))) }

function Close-AppWindow {
    param([string]$ProcessName)
    $n = 0
    foreach ($h in (Get-AppWindowHandles $ProcessName)) {
        [void][SH.Native]::PostMessage($h, $script:WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero); $n++
    }
    $n
}

function Minimize-AppWindow {
    param([string]$ProcessName)
    $n = 0
    foreach ($h in (Get-AppWindowHandles $ProcessName)) {
        [void][SH.Native]::ShowWindow($h, $script:SW_MINIMIZE); $n++
    }
    $n
}

function Maximize-AppWindow {
    param([string]$ProcessName)
    $n = 0
    foreach ($h in (Get-AppWindowHandles $ProcessName)) {
        [void][SH.Native]::ShowWindow($h, $script:SW_MAXIMIZE); $n++
    }
    $n
}

# 目标进程的某个窗口当前是否真的在前台。
function Test-AppWindowForeground {
    param([string]$ProcessName)
    $fg = [SH.Native]::GetForegroundWindow()
    foreach ($h in (Get-AppWindowHandles $ProcessName)) { if ($h -eq $fg) { return $true } }
    $false
}

# 尝试把目标窗口提到前台；仅当它【确实】到了前台才返回 $true。
# SetForegroundWindow 在现代 Windows 常因前台锁定而失败，故必须用 GetForegroundWindow 复核，
# 否则调用方会把按键误发到当前真正有焦点的别的窗口。
function Set-ForegroundAppWindow {
    param([string]$ProcessName)
    $hs = Get-AppWindowHandles $ProcessName
    if ($hs.Count -eq 0) { return $false }
    # 最小化的窗口 SetForegroundWindow 后仍是最小化——「激活置顶/发送按键」达不到目的，先还原再置前台。
    if ([SH.Native]::IsIconic($hs[0])) { [void][SH.Native]::ShowWindow($hs[0], $script:SW_RESTORE); Start-Sleep -Milliseconds 120 }
    [void][SH.Native]::SetForegroundWindow($hs[0])
    Start-Sleep -Milliseconds 120
    Test-AppWindowForeground $ProcessName
}

# 进程级「注入」互斥：发键(SendInput)/置前台(SetForegroundWindow) 分散在启动序列、动作组、单步、提醒
# 各自的后台 runspace 里跑；$script 守卫是 per-runspace 的、跨不了上下文（同 Invoke-ActionGroup 的注释）。
# 此锁只包住【单次注入动作】(发一次组合键 / 一次「置前台+复核+发键」尝试 ~200ms / 一次窗口置前台+关窗 ~120ms)，
# 【不含】等窗口出现/重试等待/延时/弹窗——故每次持锁均亚秒级、与 WaitForWindowSeconds 无关（sendkey 的重试在
# Invoke-WindowLogin 内逐次加锁，锁不跨 500ms 重试间隔）。用【等待】而非跳过：并发时后到者稍等即可，两个目标发键都能成功
# （原来并发会互抢前台致一方 Invoke-WindowLogin 复核失败而空发）。等超时(15s)则照跑不锁，宁可能并发也不挂死。
# 用显式 Enter/Exit（不传脚本块给辅助函数再 & 调用）：避免嵌套闭包捕获不到 $mods/$vk 等局部变量的老坑。
function Enter-InjectionLock {
    $m = New-Object System.Threading.Mutex($false, 'Local\rockbenben.clockwork.inject')
    $got = $false
    try { $got = $m.WaitOne(15000) } catch [System.Threading.AbandonedMutexException] { $got = $true }  # 上个持有者崩溃遗弃锁 → 接管
    [pscustomobject]@{ Mutex = $m; Got = $got }
}
function Exit-InjectionLock {
    param($Lock)
    if (-not $Lock) { return }
    if ($Lock.Got) { try { $Lock.Mutex.ReleaseMutex() } catch {} }
    try { $Lock.Mutex.Dispose() } catch {}
}

# 常用键名别名 → System.Windows.Forms.Keys 枚举正名。发键(Send-KeyCombo)与急停键注册(ConvertTo-HotkeyParams)
# 共用同一份，避免两处各存一份、加/改别名时漂移（同一拼写在发键认、注册热键不认）。
function Get-KeyNameAliasMap {
    @{ esc='Escape'; del='Delete'; ins='Insert'; bs='Back'; backspace='Back'
       pgup='PageUp'; pageup='PageUp'; pgdn='PageDown'; pagedown='PageDown'; prtsc='PrintScreen'; 'return'='Enter' }
}

# 键名 → Keys 枚举虚拟键码。发键(Send-KeyCombo)与全局急停热键注册(ConvertTo-HotkeyParams)共用，避免两处各写一份、
# 加/改别名或修 parse bug 时漂移（同一拼写「发得出、却注册不上」）。规则：多位纯数字拒绝（'10' 强转 [Keys] 会静默变
# VK 10）→ 单数字映射 D0-D9 → 别名归一 → [Keys] 枚举强转(PS5.1 无 Enum.TryParse 非泛型重载，故 try/catch)。
# 返回 [int] VK；0 = 枚举不认，由调用方各自兜底（发键退回 VkKeyScan 认单个符号字符；热键注册则判失败）。
# 刻意只用 [Keys] 枚举、不碰 SH.Native/VkKeyScan：热键注册在 GUI 启动路径，不该为它触发 SH.Native 的懒编译。
function ConvertTo-KeysVk {
    param([string]$Key)
    if ([string]::IsNullOrEmpty($Key) -or ($Key -match '^\d\d+$')) { return 0 }
    $keyName = if ($Key -match '^\d$') { "D$Key" } else { $Key }
    $alias = Get-KeyNameAliasMap
    if ($alias.ContainsKey($keyName.ToLower())) { $keyName = $alias[$keyName.ToLower()] }
    try { [int][System.Windows.Forms.Keys]$keyName } catch { 0 }
}

function Send-KeyCombo {
    param([string]$Combo)
    $parsed = ConvertFrom-KeyCombo $Combo   # 来自 Core
    # 主键缺失（如只填了 Win/Ctrl 没主键）→ 发出去也无意义，直接判失败而非假成功。
    if ([string]::IsNullOrWhiteSpace([string]$parsed.Key)) {
        Write-Warning "热键「$Combo」缺少主键，未发送"; return
    }
    # 统一走 SendInput（官方推荐，取代 keybd_event / SendKeys 组合注入）：整个组合一次原子注入，
    # 不与真实击键交错；返回注入数，0 = 被系统拒绝（UIPI/安全桌面），可如实报失败而非「发射后不管」。
    if (-not (Confirm-Win32Available)) { Write-Warning (Get-NativeRestrictedMsg); return }
    # 键名 → 虚拟键码：数字映射 D0-D9；命名键/字母走 Keys 枚举（强转+try/catch——PS5.1/.NET Framework
    # 没有 Enum.TryParse 的非泛型重载，误用会「找不到重载」且守卫被静默绕过）；单个符号字符经 VkKeyScan。
    # 多位纯数字必须先拒掉：字符串数字强转 [Keys] 按数值总能成功（'10' → VK 10），会静默注入错误按键。
    if ($parsed.Key -match '^\d\d+$') { Write-Warning "无法识别的按键名「$($parsed.Key)」（多位数字不是按键），热键「$Combo」未发送"; return }
    $needShift = $false
    $vk = [uint16](ConvertTo-KeysVk $parsed.Key)   # 共用键名→VK；0=枚举不认，下面对单个符号字符退回 VkKeyScan
    if ($vk -eq 0) {
        if ($parsed.Key.Length -eq 1) {
            $vs = [SH.Native]::VkKeyScan([char]$parsed.Key)
            if ($vs -eq -1) { Write-Warning "无法识别的按键「$($parsed.Key)」，热键「$Combo」未发送"; return }
            $vk = [uint16]($vs -band 0xFF)
            if (($vs -band 0x100) -ne 0) { $needShift = $true }   # 该字符本身需要 Shift（如 '+'）
        } else {
            Write-Warning "无法识别的按键名「$($parsed.Key)」，热键「$Combo」未发送"; return
        }
    }
    $mods = New-Object System.Collections.Generic.List[uint16]
    if ($parsed.UseWin)                                       { $mods.Add([uint16]0x5B) }  # LWIN
    if ($parsed.Modifiers -contains 'Ctrl')                   { $mods.Add([uint16]0x11) }
    if ($parsed.Modifiers -contains 'Shift' -or $needShift)   { $mods.Add([uint16]0x10) }
    if ($parsed.Modifiers -contains 'Alt')                    { $mods.Add([uint16]0x12) }
    $lk = Enter-InjectionLock
    try {
        $sent = [SH.Native]::SendCombo($mods.ToArray(), $vk)
        $expected = $mods.Count * 2 + 2
        if ($sent -eq 0) { Write-Warning "热键「$Combo」被系统拒绝注入（前台是提权窗口/安全桌面时会这样），未生效"; return }
        if ($sent -lt $expected) {
            # 部分注入（注入中途被 UIPI 拦下）：按下事件可能已进、抬起被丢 → 修饰键会卡在按下态，
            # 后续用户的每次点击都变成 Ctrl+点击/Win+点击。补发全部抬起事件善后，并如实报失败。
            [SH.Native]::ReleaseKeys((@($vk) + $mods.ToArray()))
            Write-Warning "热键「$Combo」仅部分注入（$sent/$expected），已补发按键抬起，未生效"; return
        }
        # 键已注入输入流，但目标是否真的接收/响应无法证实（焦点在哪、程序是否就绪都不可知）。
        # 返回 'unverified' 让日志如实标「已发送（未校验）」而非假 ✓。
        'unverified'
    } finally { Exit-InjectionLock $lk }
}

function Set-SystemVolume {
    param([int]$Percent)
    if (-not (Confirm-Win32Available)) { Write-Warning (Get-NativeRestrictedMsg); return }
    if ($Percent -lt 0) { $Percent = 0 }; if ($Percent -gt 100) { $Percent = 100 }
    [SH.Audio]::SetVolume([single]($Percent/100.0))
}

function Set-SystemMute {
    param([bool]$Mute)
    if (-not (Confirm-Win32Available)) { Write-Warning (Get-NativeRestrictedMsg); return }
    [SH.Audio]::SetMute($Mute)
}

function Invoke-WindowLogin {
    param([string]$Process, [string]$SendKey = '{ENTER}', [int]$TimeoutSec = 8, [switch]$Literal)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-StopRequested) { return $false }   # 急停：等窗口/重试期间收到停止即弃发（可长达 WaitForWindowSeconds）
        # 每次「置前台+复核+发键」尝试各自加进程级注入锁（~200ms/次），锁【不跨】下面的 500ms 重试等待——
        # 故即便 $TimeoutSec 很大（WaitForWindowSeconds 无上限）也不会长时间独占注入锁；复核前台仍保证并发下不误发。
        $lk = Enter-InjectionLock
        try {
            if (Set-ForegroundAppWindow $Process) {
                Start-Sleep -Milliseconds 200
                # 200ms 后焦点可能又被别的窗口抢走 → 再复核一次，仍在前台才发键。
                if (Test-AppWindowForeground $Process) {
                    # $Literal=「发送文本」：逐字、转义元字符；否则宽容解析组合键写法（'{ENTER}' 原样、'Ctrl+Enter' 自动转）
                    $seq = if ($Literal) { ConvertTo-SendKeysLiteral $SendKey } else { ConvertTo-SendKeysSequence $SendKey }
                    [System.Windows.Forms.SendKeys]::SendWait($seq)
                    return $true
                }
            }
        } finally { Exit-InjectionLock $lk }
        Start-Sleep -Milliseconds 500
    }
    $false   # 始终没能把目标窗口提到前台 → 不发任何按键，绝不误发到别处
}

# 逐字输入字面文本（「发送文本」步骤用）。$Process 留空=发给当前焦点窗口；填了则先把该进程窗口带到最前、
# 复核在前台再输入（带不到前台就不发，绝不误发到别处，复用 Invoke-WindowLogin 的置前+复核+注入锁）。空文本直接返回。
# 与发键一致：返回 'unverified'（已注入输入流，但目标是否接收无法证实）。
function Send-Text {
    param([string]$Text, [string]$Process = '')
    if ([string]::IsNullOrEmpty($Text)) { return }
    $seq = ConvertTo-SendKeysLiteral $Text
    if ([string]::IsNullOrEmpty($seq)) { return }
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    if ($Process) {
        if (-not (Confirm-Win32Available)) { Write-Warning (Get-NativeRestrictedMsg); return }
        if (Invoke-WindowLogin $Process $Text 8 -Literal) { return 'unverified' }
        # Invoke-WindowLogin 因急停返回 $false 时不误报「未能带到最前」——那是用户停的、不是没带到前台。
        if (-not (Test-StopRequested)) { Write-Warning "发送文本：未能把「$Process」窗口带到最前，未发送" }
        return
    }
    $lk = Enter-InjectionLock
    try { [System.Windows.Forms.SendKeys]::SendWait($seq) } finally { Exit-InjectionLock $lk }
    'unverified'
}

# 轮询等待某窗口出现：$Probe 返回 $true 即出现，出现即走；最多等 $TimeoutSeconds 秒（0=只探一次，保持早退语义）。
# 与 Wait-SystemReady 同规格（注入 $Probe/$Sleeper 便于纯逻辑测试，不依赖真窗口/真时钟）。探针异常视为「仍未出现」继续等，
# 最坏封顶后返回 Present=false（宁可放弃也不挂死）。返回 @{ Present; WaitedMs }。
function Wait-AppWindow {
    param(
        [int]$TimeoutSeconds = 0,
        [int]$PollMs = 500,
        [scriptblock]$Probe = $null,
        [scriptblock]$Sleeper = $null
    )
    if (-not $Probe)   { $Probe   = { $false } }
    if (-not $Sleeper) { $Sleeper = { param($ms) Start-Sleep -Milliseconds $ms } }
    if ($PollMs -lt 1) { $PollMs = 500 }
    $maxWaitMs = [Math]::Max(0, $TimeoutSeconds * 1000)
    $present = $false
    $waited  = 0
    while ($true) {
        try { $present = [bool](& $Probe) } catch { $present = $false }
        if ($present) { break }                # 窗口出现即走
        if ($waited -ge $maxWaitMs) { break }  # 到封顶仍无窗口：放弃
        if (Test-StopRequested) { break }      # 急停：不再干等窗口（返回 Present=false，调用方按「没等到」处理）
        & $Sleeper $PollMs
        $waited += $PollMs
    }
    [pscustomobject]@{ Present = $present; WaitedMs = $waited }
}

# 组合键串 -> RegisterHotKey 参数（fsModifiers/vk）。全局急停键注册用。
# 只支持 Keys 枚举可解析的键名（字母/数字/F1-F12/Enter 等命名键，含 Send-KeyCombo 同款别名）；
# 解析失败返回 $null（调用方拒注册并提示）。MOD_* 常量：Alt=1 Ctrl=2 Shift=4 Win=8。
function ConvertTo-HotkeyParams {
    param([string]$Combo)
    $p = ConvertFrom-KeyCombo $Combo   # 来自 Core
    if ([string]::IsNullOrWhiteSpace([string]$p.Key)) { return $null }
    $vk = [uint32](ConvertTo-KeysVk $p.Key)   # 共用键名→VK（多位数字拒绝 / D0-D9 / 别名 / [Keys] 枚举）
    if ($vk -eq 0) { return $null }
    $mods = [uint32]0
    if ($p.Modifiers -contains 'Alt')   { $mods = $mods -bor 0x1 }
    if ($p.Modifiers -contains 'Ctrl')  { $mods = $mods -bor 0x2 }
    if ($p.Modifiers -contains 'Shift') { $mods = $mods -bor 0x4 }
    if ($p.UseWin)                      { $mods = $mods -bor 0x8 }
    [pscustomobject]@{ Modifiers = $mods; Vk = $vk }
}

# 统一的窗口动作：一律【先激活/定位目标窗口，再执行操作】。
#   sendkey                 : 激活成功后发送按键（激活失败则不发，避免误发到别处）—— 走 Invoke-WindowLogin（自带逐次等待）。
#   close/minimize/maximize : 尽力激活后 关闭/最小化/最大化；操作针对具体窗口句柄，不依赖前台。
#   activate                : 把目标窗口带到最前面。
# WaitForWindowSeconds：close/minimize/maximize/activate 先等目标窗口出现（最多 N 秒，出现即动手；0=不等，保持早退）；
#   sendkey 时作为登录窗口的等待上限（覆盖默认 8s）。慢启动/开机自启拉起的第三方应用出窗口慢，靠它自适应等待而非盲等固定秒数。
# PostWindowDelaySeconds：窗口出现后再等这么久才动手——给「窗口先出现、随后自动登录切主窗」的应用(QQ/TIM)留时间；
#   对 close/minimize/maximize/activate 生效，sendkey 走自带流程不用它。
function Invoke-WindowAction {
    param([string]$Process, [string]$Op, [string]$SendKey = '{ENTER}', [int]$WaitForWindowSeconds = 0, [int]$PostWindowDelaySeconds = 0)
    if (-not (Confirm-Win32Available)) { Write-Warning (Get-NativeRestrictedMsg); return 0 }
    # sendkey 的等窗口+发键都在 Invoke-WindowLogin 内部逐次尝试各自加锁（每次 ~200ms），不在此处整体持锁——
    # 否则慢启动应用等窗口(可达 WaitForWindowSeconds、无上限)期间会长时间独占注入锁、把并发的其它注入饿到超时。
    if ($Op -eq 'sendkey') {
        $to = if ($WaitForWindowSeconds -gt 0) { $WaitForWindowSeconds } else { 8 }
        return (Invoke-WindowLogin $Process $SendKey $to)
    }
    if ($Op -in 'close','minimize','maximize','activate') {
        # 等窗口出现（N=0 时只探一次=原早退语义：无窗口即返回 0，批量关一串应用不累积白等）。
        # activate（带到最前面）也要等：慢启动应用窗口还没出来就 activate=空跑，用户设的「等待窗口出现」会被无视
        #（表现为「还没到就自己启动了/什么都没发生」）。sendkey 走 Invoke-WindowLogin 自带等待，故不在此列。
        # 注意：不能写 @(Get-AppWindowHandles $Process).Count —— 该函数用 ,(...) 包了一层保单元素不被拆，
        # 再套 @() 会把「那一层包装」当成 1 个元素，恒等于 1、恒 -gt 0，令本轮询【永不真正等待】慢出现的窗口。
        # 直接 (...).Count 才是真实句柄数（0 / N），与 Set-ForegroundAppWindow 的用法一致。
        $w = Wait-AppWindow -TimeoutSeconds $WaitForWindowSeconds -Probe ({ (Get-AppWindowHandles $Process).Count -gt 0 }.GetNewClosure())
        if (-not $w.Present) { return 0 }
        # 窗口已在 → 出现后延迟（登录/主窗切换就绪）再动手。急停打断延迟时不再动手（用户要停，别再关窗/发键）。
        if ($PostWindowDelaySeconds -gt 0) { if (-not (Start-InterruptibleSleep ($PostWindowDelaySeconds * 1000))) { return 0 } }
    }
    # 「置前台+动作」加进程级注入锁（~120ms/次）：等窗口出现的长等待(上面 Wait-AppWindow)留在锁外，此处只锁
    # 真正的前台切换/关窗，避免与其它 runspace 的注入交错（A 置前台后被 B 抢走前台致发键落到 B 的窗口）。
    $lk = Enter-InjectionLock
    try {
        switch ($Op) {
            'close'    { [void](Set-ForegroundAppWindow $Process); Start-Sleep -Milliseconds 120; Close-AppWindow $Process }
            'minimize' { [void](Set-ForegroundAppWindow $Process); Start-Sleep -Milliseconds 120; Minimize-AppWindow $Process }
            'maximize' { [void](Set-ForegroundAppWindow $Process); Start-Sleep -Milliseconds 120; Maximize-AppWindow $Process }
            'activate' { $hs = Get-AppWindowHandles $Process; if ($hs.Count -gt 0) { [void](Set-ForegroundAppWindow $Process) }; $hs.Count }   # 不加 @()：见上，@()+,(...) 会让 Count 恒为 1（无窗口也假报激活 1 个）
            default    { Write-Warning "未知窗口操作：$Op"; 0 }
        }
    } finally { Exit-InjectionLock $lk }
}
