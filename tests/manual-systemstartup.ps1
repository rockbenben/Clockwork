$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '..\lib\Clockwork.SystemStartup.ps1')

Write-Host ('IsElevated = ' + (Test-IsElevated))

$items = Get-SystemStartupItems
Write-Host ('共枚举到 ' + $items.Count + ' 个启动项；前 8 个：')
$items | Select-Object -First 8 name, type, scope, enabled | Format-Table -AutoSize

# —— 安全往返（仅动用户级、无害的临时项）——
$startup = [Environment]::GetFolderPath('Startup')
$probe = Join-Path $startup '__sh_probe__.txt'
Set-Content -LiteralPath $probe -Value 'clockwork probe' -Encoding UTF8
try {
    $mine = (Get-SystemStartupItems | Where-Object { $_.type -eq 'StartupFolder' -and $_.name -eq '__sh_probe__.txt' })[0]
    Write-Host ('找到探针项，初始 enabled = ' + $mine.enabled)
    Write-Host ('禁用 -> ' + (Set-SystemStartupItemEnabled $mine $false))
    $after = (Get-SystemStartupItems | Where-Object { $_.name -eq '__sh_probe__.txt' })[0]
    Write-Host ('禁用后 enabled = ' + $after.enabled + '  (应为 False)')
    Write-Host ('启用 -> ' + (Set-SystemStartupItemEnabled $after $true))
    $after2 = (Get-SystemStartupItems | Where-Object { $_.name -eq '__sh_probe__.txt' })[0]
    Write-Host ('启用后 enabled = ' + $after2.enabled + '  (应为 True)')
} finally {
    Remove-Item -LiteralPath $probe -ErrorAction SilentlyContinue
    $appPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'
    Remove-ItemProperty -LiteralPath $appPath -Name '__sh_probe__.txt' -ErrorAction SilentlyContinue
    Write-Host '已清理探针文件与 StartupApproved 记录。'
}
