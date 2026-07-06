$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\lib\StartupHelper.Core.ps1')
. (Join-Path $here '..\lib\StartupHelper.Win32.ps1')
. (Join-Path $here '..\lib\StartupHelper.Actions.ps1')

# 安全序列：开记事本 → 设音量40 → 发 Win+D 两次 → 系统命令 显示桌面
$cfg = [pscustomobject]@{ launchSteps = @(
    (New-LaunchStep 'app'    @{ label='记事本'; target='notepad.exe'; delayMs=400 }),
    (New-LaunchStep 'volume' @{ action='set'; level=40 }),
    (New-LaunchStep 'keys'   @{ combo='Win+D'; delayMs=600 }),
    (New-LaunchStep 'keys'   @{ combo='Win+D' }),
    (New-LaunchStep 'system' @{ command='showDesktop' })
) }
Write-Host '执行步骤序列 ...'
Invoke-LaunchSequence $cfg
Write-Host '完成：应开过记事本、音量到 40%、桌面显示/还原。请手动关掉记事本。'
