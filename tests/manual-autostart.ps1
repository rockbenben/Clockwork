$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\lib\Clockwork.Core.ps1')
. (Join-Path $here '..\lib\Clockwork.Win32.ps1')
. (Join-Path $here '..\lib\Clockwork.Actions.ps1')

. (Join-Path $here '..\lib\Clockwork.SystemStartup.ps1')   # Test-IsElevated

$fake = Join-Path $here '..\clockwork.ps1'
Write-Host "当前已提权? $(Test-IsElevated)（非提权应得 NeedsAdmin，提权应得 Ok 并真正建任务）"
Write-Host "注册前已存在? $(Test-AutostartRegistered)"
Write-Host "Register 返回: $(Register-Autostart (Resolve-Path $fake).Path)"
Write-Host "注册后已存在? $(Test-AutostartRegistered)"
# schtasks 而非 Get-ScheduledTask（后者走 CIM，在 WMI 不稳的机器上会挂起）
& schtasks.exe /query /tn (Get-AutostartTaskName) /fo LIST 2>&1 | Select-String 'TaskName|Status|状态'
Write-Host "Unregister 返回: $(Unregister-Autostart)"
Write-Host "删除后已存在? $(Test-AutostartRegistered)"
