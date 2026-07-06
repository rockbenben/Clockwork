# 极简断言 + 运行器：无外部依赖，pwsh / powershell 通用
$script:TestFailures = 0
$script:TestCount = 0

function Assert-Equal($Expected, $Actual, [string]$Because = '') {
    $script:TestCount++
    if ($Expected -ne $Actual) {
        $script:TestFailures++
        Write-Host "  [FAIL] $Because`n         expected=<$Expected> actual=<$Actual>" -ForegroundColor Red
    } else {
        Write-Host "  [ok]   $Because" -ForegroundColor DarkGreen
    }
}

function Assert-True($Condition, [string]$Because = '') {
    Assert-Equal $true ([bool]$Condition) $Because
}

function Invoke-TestSummary {
    Write-Host "`n$($script:TestCount) checks, $($script:TestFailures) failed."
    if ($script:TestFailures -gt 0) { exit 1 } else { exit 0 }
}
