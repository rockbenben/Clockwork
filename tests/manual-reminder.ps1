$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\lib\Clockwork.Core.ps1')
. (Join-Path $here '..\lib\Clockwork.Win32.ps1')
. (Join-Path $here '..\lib\Clockwork.Actions.ps1')

Write-Host '1) 纯通知提醒（无动作 → 系统通知）：'
Invoke-Reminder ([pscustomobject]@{ message='这是一条普通提醒'; speak=$false; onYes=@{type='none';target=''} })
Write-Host '2) 语音 + 是/否框（配了 onYes 动作 → 点是会打开计算器）：'
Invoke-Reminder ([pscustomobject]@{ message='点是打开计算器'; speak=$true; onYes=@{type='run';target='calc.exe'} })
