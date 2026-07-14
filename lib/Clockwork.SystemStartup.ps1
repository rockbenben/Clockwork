# Clockwork.SystemStartup.ps1 —— 系统启动项枚举/开关
# 本段为纯逻辑（可单测）；副作用部分在 Task 2 追加。

function Test-StartupApprovedEnabled {
    param([byte[]]$Blob)
    if (-not $Blob -or $Blob.Length -eq 0) { return $true }
    (($Blob[0] -band 0x01) -eq 0)
}

function Get-StartupApprovedBlob {
    param([bool]$Enable)
    if ($Enable) { return [byte[]](2,0,0,0,0,0,0,0,0,0,0,0) }
    [byte[]](3,0,0,0,0,0,0,0,0,0,0,0)
}

function Get-TypeLabel {
    param([string]$Type)
    switch ($Type) {
        'Registry'      { '注册表' }
        'StartupFolder' { '启动文件夹' }
        'ScheduledTask' { '计划任务' }
        default         { $Type }
    }
}

function Get-ScopeLabel {
    param([string]$Scope, [bool]$NeedsAdmin)
    $base = if ($Scope -eq 'Machine') { '所有用户' } else { '当前用户' }
    if ($NeedsAdmin) { "$base（需管理员）" } else { $base }
}

# —— 副作用部分（读注册表/文件系统/计划任务，人工验证）——

function Test-IsElevated {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object System.Security.Principal.WindowsPrincipal($id)).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ApprovedMap {
    # 一次读出某个 StartupApproved 子键的全部标志 → @{ 值名 = 是否启用 }；
    # 比逐项 Get-ItemProperty -Name（缺项还要抛异常）快得多。缺记录 = 启用，由调用方默认。
    param([string]$Hive, [string]$SubKey)
    $path = "$Hive`:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\$SubKey"
    $map = @{}
    try {
        $item = Get-ItemProperty -LiteralPath $path -ErrorAction Stop
        foreach ($p in $item.PSObject.Properties) {
            if ($p.Name -in 'PSPath','PSParentPath','PSChildName','PSDrive','PSProvider') { continue }
            $map[$p.Name] = (Test-StartupApprovedEnabled ([byte[]]$p.Value))
        }
    } catch {}
    $map
}

function Set-ApprovedState {
    param([string]$Hive, [string]$SubKey, [string]$ValueName, [bool]$Enable)
    $path = "$Hive`:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\$SubKey"
    # PS5.1 的 New-Item 没有 -LiteralPath 参数（会抛 ParameterBinding），用 -Path（此处为固定路径，无通配风险）。
    if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }
    New-ItemProperty -LiteralPath $path -Name $ValueName -Value (Get-StartupApprovedBlob $Enable) -PropertyType Binary -Force | Out-Null
}

function Get-SystemStartupItems {
    $items = New-Object System.Collections.ArrayList

    # 注册表 Run
    $runSpecs = @(
        @{ Hive='HKCU'; Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';             Scope='User';    RunKind='Run';   Approved='Run' }
        @{ Hive='HKLM'; Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\Run';             Scope='Machine'; RunKind='Run';   Approved='Run' }
        @{ Hive='HKLM'; Path='HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; Scope='Machine'; RunKind='Run32'; Approved='Run32' }
    )
    foreach ($spec in $runSpecs) {
        if (-not (Test-Path -LiteralPath $spec.Path)) { continue }
        $approved = Get-ApprovedMap $spec.Hive $spec.Approved
        $props = Get-ItemProperty -LiteralPath $spec.Path
        foreach ($p in $props.PSObject.Properties) {
            if ($p.Name -in 'PSPath','PSParentPath','PSChildName','PSDrive','PSProvider') { continue }
            $en = if ($approved.ContainsKey($p.Name)) { $approved[$p.Name] } else { $true }
            [void]$items.Add([pscustomobject]@{
                name=$p.Name; command=[string]$p.Value; type='Registry'; scope=$spec.Scope;
                enabled=$en; needsAdmin=($spec.Scope -eq 'Machine');
                regHive=$spec.Hive; regRunKind=$spec.RunKind; valueName=$p.Name;
                lnkPath=''; folderKind=''; taskName=''; taskPath=''; canToggle=$true; readOnlyNote=''
            })
        }
    }

    # 启动文件夹
    $folderSpecs = @(
        @{ Hive='HKCU'; Dir=[Environment]::GetFolderPath('Startup');       Scope='User' }
        @{ Hive='HKLM'; Dir=[Environment]::GetFolderPath('CommonStartup'); Scope='Machine' }
    )
    foreach ($spec in $folderSpecs) {
        if (-not $spec.Dir -or -not (Test-Path -LiteralPath $spec.Dir)) { continue }
        $approved = Get-ApprovedMap $spec.Hive 'StartupFolder'
        Get-ChildItem -LiteralPath $spec.Dir -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne 'desktop.ini' } | ForEach-Object {
                $en = if ($approved.ContainsKey($_.Name)) { $approved[$_.Name] } else { $true }
                [void]$items.Add([pscustomobject]@{
                    name=$_.Name; command=$_.FullName; type='StartupFolder'; scope=$spec.Scope;
                    enabled=$en; needsAdmin=($spec.Scope -eq 'Machine');
                    regHive=$spec.Hive; regRunKind=''; valueName=$_.Name;
                    lnkPath=$_.FullName; folderKind='StartupFolder'; taskName=''; taskPath=''; canToggle=$true; readOnlyNote=''
                })
            }
    }

    # 登录触发的计划任务（COM 枚举，含隐藏任务 GetTasks(1)）
    $svc = $null
    try {
        $svc = New-Object -ComObject Schedule.Service
        $svc.Connect()
        $folders = New-Object System.Collections.Queue
        $folders.Enqueue($svc.GetFolder('\'))
        while ($folders.Count -gt 0) {
            $folder = $folders.Dequeue()
            # 单个文件夹/任务读取失败(ACL 受限等)只跳过该项，不能让整段 catch 吞掉 → 否则全部计划任务从列表消失。
            try { foreach ($sub in $folder.GetFolders(0)) { $folders.Enqueue($sub) } } catch {}
            $tasks = @(); try { $tasks = @($folder.GetTasks(1)) } catch {}   # 1 = TASK_ENUM_HIDDEN
            foreach ($task in $tasks) {
                try {
                    $def = $task.Definition
                    $hasLogon = $false
                    foreach ($trg in $def.Triggers) { if ($trg.Type -eq 9) { $hasLogon = $true; break } }   # 9 = LOGON
                    if (-not $hasLogon) { continue }
                    $cmd = ''
                    foreach ($act in $def.Actions) { if ($act.Type -eq 0 -and $act.Path) { $cmd = [string]$act.Path; break } }   # 0 = EXEC
                    $scope = 'User'
                    $prin = $def.Principal
                    if ($prin.RunLevel -eq 1 -or "$($prin.UserId)" -match 'SYSTEM|S-1-5-18|S-1-5-19|S-1-5-20' -or "$($prin.GroupId)" -match 'Administrators|S-1-5-32-544') { $scope = 'Machine' }
                    $full = [string]$task.Path
                    $tname = [string]$task.Name
                    $tpath = if ($full.Length -gt $tname.Length) { $full.Substring(0, $full.Length - $tname.Length) } else { '\' }
                    [void]$items.Add([pscustomobject]@{
                        name=$tname; command=$cmd; type='ScheduledTask'; scope=$scope;
                        enabled=[bool]$task.Enabled; needsAdmin=$true;
                        regHive=''; regRunKind=''; valueName=''; lnkPath='';
                        folderKind=''; taskName=$tname; taskPath=$tpath; canToggle=$true; readOnlyNote=''
                    })
                } catch { continue }
            }
        }
    } catch { }
      finally { if ($svc) { try { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($svc) } catch {} } }

    # GPO 策略 Run（策略强制执行，无法通过 StartupApproved 关闭 -> 标只读 canToggle=$false）
    $policySpecs = @(
        @{ Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run'; Scope='User' }
        @{ Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run'; Scope='Machine' }
    )
    foreach ($spec in $policySpecs) {
        if (-not (Test-Path -LiteralPath $spec.Path)) { continue }
        $props = Get-ItemProperty -LiteralPath $spec.Path
        foreach ($p in $props.PSObject.Properties) {
            if ($p.Name -in 'PSPath','PSParentPath','PSChildName','PSDrive','PSProvider') { continue }
            [void]$items.Add([pscustomobject]@{
                name=$p.Name; command=[string]$p.Value; type='Registry'; scope=$spec.Scope;
                enabled=$true; needsAdmin=($spec.Scope -eq 'Machine');
                regHive=''; regRunKind=''; valueName=$p.Name; lnkPath='';
                folderKind=''; taskName=''; taskPath=''; canToggle=$false; readOnlyNote='策略'
            })
        }
    }

    # RunOnce / RunOnceEx（一次性，运行后自删；不可用 StartupApproved 关 -> 只读）
    # 注：RunOnceEx 的真实命令在节子键里（RunOnceEx\0001\值），此处只读键顶层值，故 RunOnceEx 多为空——只读展示用，不深挖子键。
    $onceSpecs = @(
        @{ Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce';             Scope='User';    Kind='RunOnce' }
        @{ Path='HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnceEx';           Scope='User';    Kind='RunOnceEx' }
        @{ Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce';             Scope='Machine'; Kind='RunOnce' }
        @{ Path='HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnceEx';           Scope='Machine'; Kind='RunOnceEx' }
        @{ Path='HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce'; Scope='Machine'; Kind='RunOnce32' }
    )
    foreach ($spec in $onceSpecs) {
        try {
            if (-not (Test-Path -LiteralPath $spec.Path)) { continue }
            $props = Get-ItemProperty -LiteralPath $spec.Path -ErrorAction Stop
            foreach ($p in $props.PSObject.Properties) {
                if ($p.Name -in 'PSPath','PSParentPath','PSChildName','PSDrive','PSProvider') { continue }
                [void]$items.Add([pscustomobject]@{
                    name=$p.Name; command=[string]$p.Value; type='Registry'; scope=$spec.Scope;
                    enabled=$true; needsAdmin=($spec.Scope -eq 'Machine');
                    regHive=''; regRunKind=''; valueName=$p.Name; lnkPath='';
                    folderKind=''; taskName=''; taskPath=''; canToggle=$false; readOnlyNote='一次性'
                })
            }
        } catch {}
    }

    # Winlogon Shell / Userinit（系统关键值 -> 只读展示）
    try {
        $wlPath = 'HKLM:\Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
        if (Test-Path -LiteralPath $wlPath) {
            $wl = Get-ItemProperty -LiteralPath $wlPath -ErrorAction Stop
            foreach ($v in 'Shell','Userinit') {
                if ($wl.PSObject.Properties[$v] -and "$($wl.$v)" -ne '') {
                    [void]$items.Add([pscustomobject]@{
                        name="Winlogon $v"; command=[string]$wl.$v; type='Registry'; scope='Machine';
                        enabled=$true; needsAdmin=$true;
                        regHive=''; regRunKind=''; valueName=$v; lnkPath='';
                        folderKind=''; taskName=''; taskPath=''; canToggle=$false; readOnlyNote='系统关键'
                    })
                }
            }
        }
    } catch {}

    # Active Setup StubPath（每用户首登初始化 -> 只读展示）
    $asSpecs = @(
        'HKLM:\Software\Microsoft\Active Setup\Installed Components'
        'HKLM:\Software\WOW6432Node\Microsoft\Active Setup\Installed Components'
    )
    foreach ($base in $asSpecs) {
        try {
            if (-not (Test-Path -LiteralPath $base)) { continue }
            foreach ($sub in (Get-ChildItem -LiteralPath $base -ErrorAction Stop)) {
                $stub = $null
                try { $stub = (Get-ItemProperty -LiteralPath $sub.PSPath -Name 'StubPath' -ErrorAction Stop).StubPath } catch { continue }
                if ([string]::IsNullOrWhiteSpace($stub)) { continue }
                $disp = $null
                try { $disp = (Get-ItemProperty -LiteralPath $sub.PSPath -ErrorAction Stop).'(default)' } catch {}
                $nm = if ($disp) { [string]$disp } else { $sub.PSChildName }
                [void]$items.Add([pscustomobject]@{
                    name=$nm; command=[string]$stub; type='Registry'; scope='Machine';
                    enabled=$true; needsAdmin=$true;
                    regHive=''; regRunKind=''; valueName=$sub.PSChildName; lnkPath='';
                    folderKind=''; taskName=''; taskPath=''; canToggle=$false; readOnlyNote='Active Setup'
                })
            }
        } catch {}
    }

    ,$items.ToArray()
}

function Set-SystemStartupItemEnabled {
    param($Item, [bool]$Enable)
    try {
        switch ($Item.type) {
            'Registry'      { Set-ApprovedState $Item.regHive $Item.regRunKind $Item.valueName $Enable }
            'StartupFolder' { Set-ApprovedState $Item.regHive 'StartupFolder'  $Item.valueName $Enable }
            'ScheduledTask' {
                # 用 schtasks.exe（直连任务计划服务）而非 Enable/Disable-ScheduledTask——后者走 CIM，
                # 在 WMI 不稳的机器上会挂起数分钟（与本项目把自启注册/检测换成 schtasks 同因）。
                $tn = [string]$Item.taskPath + [string]$Item.taskName
                $out = if ($Enable) { & schtasks.exe /change /tn $tn /enable 2>&1 } else { & schtasks.exe /change /tn $tn /disable 2>&1 }
                if ($LASTEXITCODE -ne 0) { throw ("schtasks: " + ($out -join ' ')) }   # 「拒绝访问」由下方 catch 统一映射 NeedsAdmin
            }
        }
        return 'Ok'
    } catch [System.UnauthorizedAccessException] { return 'NeedsAdmin' }
      catch [System.Security.SecurityException]  { return 'NeedsAdmin' }
      catch {
        $msg = "$($_.Exception.Message)"
        if ($msg -match 'denied|Access is denied|0x80070005|拒绝|权限') { return 'NeedsAdmin' }
        return "Error: $msg"
      }
}

function Restart-Elevated {
    param([string]$ScriptPath)
    try {
        # 经 conhost --headless 提权重开：Win11 默认终端无视 powershell 的 -WindowStyle Hidden、会留一个
        # 不消失的终端窗口；conhost --headless 无窗口运行且不移交 Windows Terminal（提权后 powershell 继承管理员令牌）。
        Start-Process conhost.exe -Verb RunAs -ArgumentList @('--headless','powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $ScriptPath + '"')) -ErrorAction Stop | Out-Null
        return $true
    } catch { return $false }
}
