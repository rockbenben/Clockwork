$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\lib\Clockwork.Core.ps1')
. (Join-Path $here '..\lib\Clockwork.Win32.ps1')

Write-Host '设音量 30% ...'; Set-SystemVolume 30; Start-Sleep 1
Write-Host '设音量 70% ...'; Set-SystemVolume 70; Start-Sleep 1
Write-Host '静音 ...';       Set-SystemMute $true; Start-Sleep 1
Write-Host '取消静音 ...';   Set-SystemMute $false
Write-Host '发送 Win+D（显示桌面，再发一次还原）...'; Send-KeyCombo 'Win+D'; Start-Sleep 1; Send-KeyCombo 'Win+D'
Write-Host '完成。请确认：音量条到过 30→70、静音灯亮灭、桌面被显示/还原。'