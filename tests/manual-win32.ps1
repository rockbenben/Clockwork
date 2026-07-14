$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\lib\Clockwork.Core.ps1')
. (Join-Path $here '..\lib\Clockwork.Win32.ps1')

Write-Host '打开记事本，3 秒后最小化它...'
Start-Process notepad.exe; Start-Sleep 3
$n = Minimize-AppWindow 'notepad'
Write-Host "minimized $n notepad window(s) —— 记事本应已最小化"
Start-Sleep 2
Write-Host '激活记事本...'; [void](Set-ForegroundAppWindow 'notepad'); Start-Sleep 2
Write-Host '关闭记事本...'; $c = Close-AppWindow 'notepad'
Write-Host "closed $c notepad window(s)"
Write-Host '测试发送 Win+6（应打开任务栏第 6 个程序，无则忽略）...'; Send-KeyCombo 'Win+6'
