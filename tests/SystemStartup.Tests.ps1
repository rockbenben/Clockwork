$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here '_assert.ps1')
. (Join-Path $here '..\lib\StartupHelper.SystemStartup.ps1')

Write-Host 'Test-StartupApprovedEnabled'
Assert-True  (Test-StartupApprovedEnabled $null)                                      'null = enabled'
Assert-True  (Test-StartupApprovedEnabled ([byte[]]@()))                              'empty = enabled'
Assert-True  (Test-StartupApprovedEnabled ([byte[]](2,0,0,0,0,0,0,0,0,0,0,0)))        'byte0=2 -> enabled'
Assert-True  (-not (Test-StartupApprovedEnabled ([byte[]](3,0,0,0,0,0,0,0,0,0,0,0)))) 'byte0=3 -> disabled'
Assert-True  (Test-StartupApprovedEnabled ([byte[]](6,0,0,0,0,0,0,0,0,0,0,0)))        'byte0=6 even -> enabled'
Assert-True  (-not (Test-StartupApprovedEnabled ([byte[]](7,0,0,0,0,0,0,0,0,0,0,0)))) 'byte0=7 odd -> disabled'

Write-Host 'Get-StartupApprovedBlob'
$en = Get-StartupApprovedBlob $true; $di = Get-StartupApprovedBlob $false
Assert-Equal 12 $en.Length 'enabled blob 12 bytes'
Assert-Equal 2  $en[0]     'enabled blob byte0=2'
Assert-Equal 3  $di[0]     'disabled blob byte0=3'
Assert-True  (Test-StartupApprovedEnabled (Get-StartupApprovedBlob $true))            'round-trip enabled'
Assert-True  (-not (Test-StartupApprovedEnabled (Get-StartupApprovedBlob $false)))    'round-trip disabled'

Write-Host 'labels'
Assert-Equal '注册表'   (Get-TypeLabel 'Registry')      'type registry'
Assert-Equal '计划任务' (Get-TypeLabel 'ScheduledTask') 'type task'
Assert-Equal '所有用户（需管理员）' (Get-ScopeLabel 'Machine' $true)  'scope machine+admin'
Assert-Equal '当前用户' (Get-ScopeLabel 'User' $false)  'scope user'

Invoke-TestSummary
