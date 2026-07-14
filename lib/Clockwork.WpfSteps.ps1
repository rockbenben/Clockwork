# Clockwork.WpfSteps.ps1 —— 各类步骤对话框（WPF）+ 步骤派发 + 动作组编辑器。配合 WpfGui/WpfDialogs。

function New-DlgCombo {
    param([string[]]$Labels, [string[]]$Values, [string]$Selected, [double]$Width=200)
    $cb=New-Object System.Windows.Controls.ComboBox; $cb.Width=$Width; $cb.HorizontalAlignment='Left'; $cb.Height=30; $cb.FontSize=14
    for($i=0;$i -lt $Labels.Count;$i++){ $it=New-Object System.Windows.Controls.ComboBoxItem; $it.Content=$Labels[$i]; $it.Tag=[string]$Values[$i]; [void]$cb.Items.Add($it) }
    $idx=[array]::IndexOf([string[]]$Values,[string]$Selected); if($idx -lt 0){$idx=0}; $cb.SelectedIndex=$idx
    $cb
}
function Get-ComboValue { param($Cb) if($Cb.SelectedItem){ [string]$Cb.SelectedItem.Tag } else { '' } }
# 延时行（步骤尾部通用）——返回延时 TextBox
function Add-DlgDelayRow { param($Body, $DelayMs) $t=New-DlgText ([string][int]$DelayMs); $t.Width=110; $t.HorizontalAlignment='Left'; Add-DlgRow $Body '执行后延时(ms)' $t | Out-Null; $t }

function Show-KeysStepDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'keys' }
    $dlg=New-WpfDialog '编辑 · 发送按键' 560 $Owner; $body=$dlg.FindName('Body')
    $t=Add-DlgCaptureRow $body '组合键' $Step.combo
    $h=New-Object System.Windows.Controls.TextBlock; $h.Text='例：Win+D / Alt+K / Ctrl+Enter / F5（支持 Enter、Tab、Esc、Del、方向键等键名）。点「捕获」按下快捷键即自动填入；Win+ 组合会被系统截走、需手输，符号键也请手输。按键发给【当前焦点窗口】——请自己先确保目标窗口在最前、光标就位（可配前一步「延时」或「窗口动作 · 带到最前面」）。'; $h.Foreground=$script:MutedBrush; $h.FontSize=12; $h.TextWrapping='Wrap'; Add-DlgRow $body $null $h | Out-Null
    $inm=Add-DlgIconNoteRows $body $Step
    $dl=Add-DlgDelayRow $body $Step.delayMs
    $rp=Add-DlgRepeatRow $body $Step
    $cond=Add-DlgCondRows $body $Step
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($dl.Text,[ref]$d); $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'keys' @{ enabled=[bool]$Step.enabled; label=$t.Text; combo=$t.Text; delayMs=$d; repeat=(Get-DlgRepeatValue $rp); days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}
function Show-VolumeStepDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'volume' @{ action='set' } }
    $dlg=New-WpfDialog '编辑 · 音量' 520 $Owner; $body=$dlg.FindName('Body')
    $cb=New-DlgCombo @('静音','取消静音','设为音量') @('mute','unmute','set') $Step.action 160; Add-DlgRow $body '动作' $cb | Out-Null
    $lv=New-DlgText ([string][int]$Step.level); $lv.Width=90; $lv.HorizontalAlignment='Left'; $rowLv=Add-DlgRow $body '音量(0-100)' $lv
    $dl=Add-DlgDelayRow $body $Step.delayMs
    $cond=Add-DlgCondRows $body $Step
    # 音量行仅在「设为音量」时显示——静音/取消静音用不到，摆着只会让人困惑
    $togLv={ $rowLv.Visibility = if((Get-ComboValue $cb) -eq 'set'){'Visible'}else{'Collapsed'} }.GetNewClosure()
    $cb.Add_SelectionChanged($togLv); & $togLv
    $inm=Add-DlgIconNoteRows $body $Step
    $rp=Add-DlgRepeatRow $body $Step
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($dl.Text,[ref]$d); $lvl=0;[void][int]::TryParse($lv.Text,[ref]$lvl); $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'volume' @{ enabled=[bool]$Step.enabled; action=(Get-ComboValue $cb); level=[Math]::Max(0,[Math]::Min(100,$lvl)); delayMs=$d; repeat=(Get-DlgRepeatValue $rp); days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}
function Show-WindowStepDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'window' @{ action='close' } }
    $dlg=New-WpfDialog '编辑 · 窗口动作' 560 $Owner; $body=$dlg.FindName('Body')
    $cb=New-DlgCombo @('关闭窗口','最小化窗口','最大化窗口','带到最前面','置前并发送按键') @('close','minimize','maximize','activate','sendkey') $Step.action 190; Add-DlgRow $body '动作' $cb | Out-Null
    $tp=Add-DlgBrowseRow $body '进程名' $Step.process { param($cur) Select-ProcessNameDialog $dlg } '选择…'
    $h=New-Object System.Windows.Controls.TextBlock; $h.Text='进程名，不含 .exe，如 Weixin / QQ / msedge（任务管理器「详细信息」列可查）。注：「带到最前面 / 置前并发送按键」需抢占前台，开机自启或后台触发时系统可能不允许（只闪任务栏、不动作）——目标程序已在前台时最稳。'; $h.Foreground=$script:MutedBrush; $h.FontSize=12; $h.TextWrapping='Wrap'; Add-DlgRow $body $null $h | Out-Null
    $tk=Add-DlgCaptureRow $body '发送按键' $Step.sendKey; $rowSend=$tk.Row
    $hk=New-Object System.Windows.Controls.TextBlock; $hk.Text='可写 Enter / Ctrl+Enter 这类组合，或原生 SendKeys 序列（如 {ENTER}、hello{TAB}）。点「捕获」可录单个组合键（Win+ 组合与连续序列请手输）。会先把该程序窗口带到最前再发；带不到最前（开机自启/后台触发常见）则不发，请在程序已在前台时用。'; $hk.Foreground=$script:MutedBrush; $hk.FontSize=12; $hk.TextWrapping='Wrap'; $rowSendHint=Add-DlgRow $body $null $hk
    $tw=New-DlgText ([string][int]$Step.waitForWindowSeconds); Add-DlgRow $body '等待窗口出现(秒)' $tw | Out-Null
    $hw=New-Object System.Windows.Controls.TextBlock; $hw.Text='最多等目标程序的窗口出现这么多秒，一出现就动手。填 0 = 不等（现在有窗口就动手，没有就跳过）；开机时程序起得慢就填大点（如 120），窗口一冒出来就动、不会白等满'; $hw.Foreground=$script:MutedBrush; $hw.FontSize=12; $hw.TextWrapping='Wrap'; Add-DlgRow $body $null $hw | Out-Null
    $tpd=New-DlgText ([string][int]$Step.postWindowDelaySeconds); $rowPost=Add-DlgRow $body '出现后再等(秒)' $tpd
    $hpd=New-Object System.Windows.Controls.TextBlock; $hpd.Text='窗口出现后，再多等几秒才动手。像 QQ、TIM 会先弹个小窗、过一两秒才切到主界面，等一下才能操作到主界面（填 5 左右）。发送按键不用设这个'; $hpd.Foreground=$script:MutedBrush; $hpd.FontSize=12; $hpd.TextWrapping='Wrap'; $rowPostHint=Add-DlgRow $body $null $hpd
    $dl=Add-DlgDelayRow $body $Step.delayMs
    $cond=Add-DlgCondRows $body $Step
    $toggle={ $v = if((Get-ComboValue $cb) -eq 'sendkey'){'Visible'}else{'Collapsed'}; $rowSend.Visibility=$v; $rowSendHint.Visibility=$v; $pv = if((Get-ComboValue $cb) -in 'close','minimize','maximize','activate'){'Visible'}else{'Collapsed'}; $rowPost.Visibility=$pv; $rowPostHint.Visibility=$pv }.GetNewClosure()
    $cb.Add_SelectionChanged($toggle); & $toggle
    $inm=Add-DlgIconNoteRows $body $Step
    $rp=Add-DlgRepeatRow $body $Step
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($dl.Text,[ref]$d); $wv=0;[void][int]::TryParse($tw.Text,[ref]$wv); $pv=0;[void][int]::TryParse($tpd.Text,[ref]$pv); $act=(Get-ComboValue $cb); $actLb=[string]$cb.SelectedItem.Content; $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'window' @{ enabled=[bool]$Step.enabled; action=$act; process=(ConvertTo-ProcessName $tp.Text); sendKey=$tk.Text; waitForWindowSeconds=$wv; postWindowDelaySeconds=$pv; label="$actLb $(ConvertTo-ProcessName $tp.Text)"; delayMs=$d; repeat=(Get-DlgRepeatValue $rp); days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}
function Show-SystemStepDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'system' }
    $dlg=New-WpfDialog '编辑 · 系统命令' 520 $Owner; $body=$dlg.FindName('Body')
    $m=Get-SystemCommandMap; $ids=@($m.Keys); $labels=@($ids | ForEach-Object { $m[$_] })
    $cb=New-DlgCombo $labels $ids $Step.command 220; Add-DlgRow $body '命令' $cb | Out-Null
    $dl=Add-DlgDelayRow $body $Step.delayMs
    $cond=Add-DlgCondRows $body $Step
    $inm=Add-DlgIconNoteRows $body $Step
    $rp=Add-DlgRepeatRow $body $Step
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($dl.Text,[ref]$d); $cmd=(Get-ComboValue $cb); $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'system' @{ enabled=[bool]$Step.enabled; command=$cmd; label=$m[$cmd]; delayMs=$d; repeat=(Get-DlgRepeatValue $rp); days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}
function Show-GroupStepDialogWpf {
    param($Step, $Groups, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'group' }
    $dlg=New-WpfDialog '编辑 · 动作组' 520 $Owner; $body=$dlg.FindName('Body')
    $gs=@($Groups); $labels=@('（无）')+@($gs|ForEach-Object{[string]$_.name}); $vals=@('')+@($gs|ForEach-Object{[string]$_.id})
    $cb=New-DlgCombo $labels $vals $Step.groupId 240; Add-DlgRow $body '动作组' $cb | Out-Null
    $h=New-Object System.Windows.Controls.TextBlock; $h.Text='开机 / 重跑启动清单时运行该组全部步骤（组内消息步骤此时跳过，不弹窗打断）'; $h.Foreground=$script:MutedBrush; $h.FontSize=12; $h.TextWrapping='Wrap'; Add-DlgRow $body $null $h | Out-Null
    $dl=Add-DlgDelayRow $body $Step.delayMs
    $cond=Add-DlgCondRows $body $Step
    $inm=Add-DlgIconNoteRows $body $Step
    $rp=Add-DlgRepeatRow $body $Step
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($dl.Text,[ref]$d); $gid=(Get-ComboValue $cb); $gn=($gs|Where-Object{[string]$_.id -eq $gid}|ForEach-Object{[string]$_.name}); $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'group' @{ enabled=[bool]$Step.enabled; groupId=$gid; label=[string]$gn; delayMs=$d; repeat=(Get-DlgRepeatValue $rp); days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}
function Show-MessageStepDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'message' }
    $dlg=New-WpfDialog '编辑 · 消息' 580 $Owner; $body=$dlg.FindName('Body')
    $tm=New-Object System.Windows.Controls.TextBox; $tm.Text=[string]$Step.message; $tm.AcceptsReturn=$true; $tm.TextWrapping='Wrap'; $tm.Height=90; $tm.VerticalScrollBarVisibility='Auto'; Add-DlgRow $body '文本' $tm | Out-Null
    $cSpk=New-Object System.Windows.Controls.CheckBox; $cSpk.Content='语音播报'; $cSpk.Foreground=$script:InkBrush; $cSpk.IsChecked=[bool]$Step.speak; Add-DlgRow $body $null $cSpk | Out-Null
    $cCfm=New-Object System.Windows.Controls.CheckBox; $cCfm.Content='是/否确认（是=继续，否=中止本组；配了「点是后」时总会询问）'; $cCfm.Foreground=$script:InkBrush; $cCfm.IsChecked=[bool]$Step.confirm; Add-DlgRow $body $null $cCfm | Out-Null
    $yTypeSel = if ([string]$Step.onYes.type -eq 'sound') { 'run' } else { [string]$Step.onYes.type }   # 旧 sound 并入 run
    $cbY=New-DlgCombo @('无','运行 / 打开文件','开网页') @('none','run','url') $yTypeSel 130
    $tY=New-DlgText $Step.onYes.target
    $rowY=New-Object System.Windows.Controls.Grid; $rowY.Margin='0,0,0,12'
    $c0=New-Object System.Windows.Controls.ColumnDefinition;$c0.Width='116'; $c1=New-Object System.Windows.Controls.ColumnDefinition;$c1.Width='130'; $c2=New-Object System.Windows.Controls.ColumnDefinition;$c2.Width='*'
    $rowY.ColumnDefinitions.Add($c0);$rowY.ColumnDefinitions.Add($c1);$rowY.ColumnDefinitions.Add($c2)
    $lb=New-Object System.Windows.Controls.TextBlock;$lb.Text='点是后';$lb.Foreground=$script:InkBrush;$lb.VerticalAlignment='Center';$lb.FontSize=14;[System.Windows.Controls.Grid]::SetColumn($lb,0);[void]$rowY.Children.Add($lb)
    [System.Windows.Controls.Grid]::SetColumn($cbY,1);[void]$rowY.Children.Add($cbY)
    $tYWrap=New-Object System.Windows.Controls.Grid; $tYWrap.Margin='8,0,0,0'
    $wc0=New-Object System.Windows.Controls.ColumnDefinition; $wc0.Width='*'; $wc1=New-Object System.Windows.Controls.ColumnDefinition; $wc1.Width='Auto'
    $tYWrap.ColumnDefinitions.Add($wc0); $tYWrap.ColumnDefinitions.Add($wc1)
    [System.Windows.Controls.Grid]::SetColumn($tY,0);[void]$tYWrap.Children.Add($tY)
    $btnYB=New-Object System.Windows.Controls.Button; $btnYB.Content='…'; $btnYB.ToolTip='浏览…'; $btnYB.Style=$dlg.FindResource('Ghost'); $btnYB.Height=30; $btnYB.MinWidth=36; $btnYB.Margin='8,0,0,0'
    [System.Windows.Controls.Grid]::SetColumn($btnYB,1);[void]$tYWrap.Children.Add($btnYB)
    $btnYB.Add_Click({ $r=Select-FilePathDialog $tY.Text; if($r){ $tY.Text=[string]$r } }.GetNewClosure())
    [System.Windows.Controls.Grid]::SetColumn($tYWrap,2);[void]$rowY.Children.Add($tYWrap)
    $togYB={ $btnYB.Visibility=$(if((Get-ComboValue $cbY) -eq 'run'){'Visible'}else{'Collapsed'}) }.GetNewClosure()
    $cbY.Add_SelectionChanged($togYB); & $togYB
    [void]$body.Children.Add($rowY)
    $inm=Add-DlgIconNoteRows $body $Step
    $dl=Add-DlgDelayRow $body $Step.delayMs
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($dl.Text,[ref]$d); $box.R=New-LaunchStep 'message' @{ enabled=[bool]$Step.enabled; message=$tm.Text; speak=[bool]$cSpk.IsChecked; confirm=[bool]$cCfm.IsChecked; onYes=@{ type=(Get-ComboValue $cbY); target=$tY.Text }; delayMs=$d; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}

# 纯延时步骤：只填等待秒数（存进 delayMs，复用分步延时机制真正 Start-Sleep）。放到清单最前面即可整体推迟启动。
function Show-DelayStepDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'delay' @{ delayMs=60000 } }
    $dlg=New-WpfDialog '编辑 · 延时' 520 $Owner; $body=$dlg.FindName('Body')
    $sec=[int][Math]::Round(([int]$Step.delayMs)/1000)   # delayMs 存的即本步等待时长，回显为秒
    $t=New-DlgText ([string]$sec); $t.Width=110; $t.HorizontalAlignment='Left'; Add-DlgRow $body '延时（秒）' $t | Out-Null
    $h=New-Object System.Windows.Controls.TextBlock; $h.Text='到这一步先等待指定秒数再继续。放在清单最前面即可整体推迟启动（如开得太早，先等 60 秒再启动后面的程序）。'; $h.Foreground=$script:MutedBrush; $h.FontSize=12; $h.TextWrapping='Wrap'; Add-DlgRow $body $null $h | Out-Null
    $cond=Add-DlgCondRows $body $Step
    $inm=Add-DlgIconNoteRows $body $Step
    $box=@{R=$null}
    # 上限 2147483 秒：再大 $sv*1000 会溢出 Int32→被 PS 提升为 Double，之后回显(本函数 :115)/运行(Actions 里
    # Start-Sleep 的 [int]$step.delayMs)时转换抛 OverflowException——该延时步骤再也打不开、开机序列跑到它就崩。夹到 Int32 毫秒内即安全。
    Add-DlgButtons $dlg $body ({ $sv=0;[void][int]::TryParse($t.Text,[ref]$sv); if($sv -lt 0){$sv=0}elseif($sv -gt 2147483){$sv=2147483}; $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'delay' @{ enabled=[bool]$Step.enabled; label="延时 $sv 秒"; delayMs=($sv*1000); days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}

# 发送文本：往当前焦点窗口逐字输入的字面文本（多行）。执行时用 SendKeys.SendWait。
function Show-TextStepDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'text' }
    $dlg=New-WpfDialog '编辑 · 发送文本' 580 $Owner; $body=$dlg.FindName('Body')
    $tm=New-Object System.Windows.Controls.TextBox; $tm.Text=[string]$Step.text; $tm.AcceptsReturn=$true; $tm.TextWrapping='Wrap'; $tm.Height=110; $tm.VerticalScrollBarVisibility='Auto'; Add-DlgRow $body '文本' $tm | Out-Null
    $h=New-Object System.Windows.Controls.TextBlock; $h.Text='逐字输入文本（换行=回车、Tab 生效）。'; $h.Foreground=$script:MutedBrush; $h.FontSize=12; $h.TextWrapping='Wrap'; Add-DlgRow $body $null $h | Out-Null
    $tp=Add-DlgBrowseRow $body '目标进程' $Step.process { param($cur) Select-ProcessNameDialog $dlg } '选择…'
    $hp=New-Object System.Windows.Controls.TextBlock; $hp.Text='留空 =【推荐·最稳】直接发给当前焦点窗口（自己先把光标点进目标输入框，可配前一步「延时」）。填进程名（不含 .exe）会先尝试把它带到最前再输入——但开机自启/后台触发时系统常不让抢前台（只闪任务栏、不输入），这类场景请改为留空、自行聚焦。'; $hp.Foreground=$script:MutedBrush; $hp.FontSize=12; $hp.TextWrapping='Wrap'; Add-DlgRow $body $null $hp | Out-Null
    $dl=Add-DlgDelayRow $body $Step.delayMs
    $cond=Add-DlgCondRows $body $Step
    $inm=Add-DlgIconNoteRows $body $Step
    $rp=Add-DlgRepeatRow $body $Step
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($dl.Text,[ref]$d); $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'text' @{ enabled=[bool]$Step.enabled; text=$tm.Text; process=(ConvertTo-ProcessName $tp.Text); delayMs=$d; repeat=(Get-DlgRepeatValue $rp); days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}

# 按 kind 派发到对应步骤对话框。返回新步骤或 $null。
function Show-StepDialogWpf {
    param([string]$Kind, $Step, $Groups, $Owner)
    switch ($Kind) {
        'app'     { Show-LaunchItemDialogWpf $Step $Owner }
        'keys'    { Show-KeysStepDialogWpf $Step $Owner }
        'volume'  { Show-VolumeStepDialogWpf $Step $Owner }
        'window'  { Show-WindowStepDialogWpf $Step $Owner }
        'system'  { Show-SystemStepDialogWpf $Step $Owner }
        'group'   { Show-GroupStepDialogWpf $Step $Groups $Owner }
        'delay'   { Show-DelayStepDialogWpf $Step $Owner }
        'text'    { Show-TextStepDialogWpf $Step $Owner }
        'message' { Show-MessageStepDialogWpf $Step $Owner }
        default   { Show-LaunchItemDialogWpf $Step $Owner }
    }
}

# 动作组编辑器：名称 + 步骤列表(增▾/改/删/上/下) + 确定/取消。覆盖 WpfDialogs 里的占位实现。
function Show-ActionGroupDialogWpf {
    param($Group, $Owner)
    if ($null -eq $Group) { $Group = New-ActionGroup }
    $x = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
  Title="编辑动作组" Width="720" Height="560" WindowStartupLocation="CenterOwner" Background="#22262D"
  WindowStyle="ToolWindow" ResizeMode="CanResize" MinWidth="560" MinHeight="420" FontFamily="Microsoft YaHei UI" TextOptions.TextFormattingMode="Display">
  <Grid Margin="18">
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,6">
      <TextBlock Text="名称" Foreground="#EAEDF1" VerticalAlignment="Center" FontSize="14" Width="52"/>
      <TextBox x:Name="Name" Width="420" Height="30" Background="#2A2F37" Foreground="#EAEDF1" BorderBrush="#353C45" BorderThickness="1" Padding="6,4" FontSize="14" CaretBrush="#EAEDF1"/>
    </StackPanel>
    <TextBlock Grid.Row="1" Text="动作组只定义动作；触发请在「启动清单」或「提醒」里引用本组。" Foreground="#98A2AE" FontSize="12" Margin="52,0,0,10"/>
    <Grid Grid.Row="2">
      <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="110"/></Grid.ColumnDefinitions>
      <DataGrid x:Name="Steps" Grid.Column="0" AutoGenerateColumns="False" CanUserAddRows="False" HeadersVisibility="Column" GridLinesVisibility="Horizontal" HorizontalGridLinesBrush="#2A3038" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                Background="#22262D" Foreground="#EAEDF1" BorderThickness="1" BorderBrush="#353C45" RowBackground="#22262D" RowHeight="36" FontSize="13" SelectionMode="Single">
        <DataGrid.Columns>
          <DataGridTemplateColumn Header="类型" Width="112"><DataGridTemplateColumn.CellTemplate><DataTemplate><Border Background="#2E343C" BorderBrush="#353C45" BorderThickness="1" CornerRadius="7" Padding="9,2" HorizontalAlignment="Left" VerticalAlignment="Center"><TextBlock Text="{Binding T1}" Foreground="#C7CDD5" FontSize="12.5"/></Border></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
          <DataGridTextColumn Header="摘要" Binding="{Binding T2}" Width="*" IsReadOnly="True"><DataGridTextColumn.ElementStyle><Style TargetType="TextBlock"><Setter Property="VerticalAlignment" Value="Center"/><Setter Property="TextTrimming" Value="CharacterEllipsis"/><Setter Property="ToolTip" Value="{Binding Text, RelativeSource={RelativeSource Self}}"/></Style></DataGridTextColumn.ElementStyle></DataGridTextColumn>
          <DataGridTextColumn Header="延时" Binding="{Binding T3}" Width="86" IsReadOnly="True"><DataGridTextColumn.ElementStyle><Style TargetType="TextBlock"><Setter Property="VerticalAlignment" Value="Center"/><Setter Property="FontFamily" Value="Consolas"/><Setter Property="FontSize" Value="12.5"/><Setter Property="Foreground" Value="#98A2AE"/><Setter Property="TextAlignment" Value="Right"/><Setter Property="Margin" Value="0,0,6,0"/></Style></DataGridTextColumn.ElementStyle></DataGridTextColumn>
        </DataGrid.Columns>
        <DataGrid.Resources>
          <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#22262D"/><Setter Property="Foreground" Value="#6B7480"/><Setter Property="FontWeight" Value="SemiBold"/><Setter Property="FontSize" Value="12"/><Setter Property="BorderThickness" Value="0,0,0,1"/><Setter Property="BorderBrush" Value="#353C45"/><Setter Property="Padding" Value="10,7"/><Setter Property="Height" Value="32"/></Style>
          <Style TargetType="DataGridCell"><Setter Property="BorderThickness" Value="0"/><Setter Property="Foreground" Value="#EAEDF1"/><Setter Property="VerticalContentAlignment" Value="Center"/><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#382718"/></Trigger></Style.Triggers></Style>
        </DataGrid.Resources>
      </DataGrid>
      <StackPanel Grid.Column="1" Margin="10,0,0,0">
        <Button x:Name="SAdd" Content="新增 ▾" Height="32" Margin="0,0,0,8" Foreground="#EAEDF1" Cursor="Hand"><Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2A2F37" BorderBrush="#353C45" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#F0651A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>
        <Button x:Name="SEdit" Content="编辑" Height="32" Margin="0,0,0,8" Foreground="#EAEDF1" Cursor="Hand"><Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2A2F37" BorderBrush="#353C45" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#F0651A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>
        <Button x:Name="SDel" Content="删除" Height="32" Margin="0,0,0,8" Foreground="#EAEDF1" Cursor="Hand"><Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2A2F37" BorderBrush="#353C45" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#F0651A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>
        <Button x:Name="SUp" Content="上移" Height="32" Margin="0,14,0,8" Foreground="#EAEDF1" Cursor="Hand"><Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2A2F37" BorderBrush="#353C45" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#F0651A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>
        <Button x:Name="SDown" Content="下移" Height="32" Foreground="#EAEDF1" Cursor="Hand"><Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2A2F37" BorderBrush="#353C45" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#F0651A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>
      </StackPanel>
    </Grid>
    <StackPanel Grid.Row="3" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,14,0,0">
      <Button x:Name="Ok" Content="确定" MinWidth="92" Height="36" Margin="0,0,10,0" Foreground="#1A1D22" FontWeight="Bold" Cursor="Hand"><Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#F0651A" CornerRadius="8" Padding="14,0"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#FF7C34"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>
      <Button x:Name="Cancel" Content="取消" MinWidth="80" Height="34" Foreground="#EAEDF1" Cursor="Hand"><Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2A2F37" BorderBrush="#353C45" BorderThickness="1" CornerRadius="8" Padding="14,0"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#F0651A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template></Button>
    </StackPanel>
  </Grid>
</Window>
'@
    $dlg=[Windows.Markup.XamlReader]::Parse($x)
    $dlg.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse($script:DlgRes))   # 复用对话框资源：主要为纤细滚动条（编辑器的内联控件多已自带样式，隐式样式仅补齐滚动条等）
    try { if ($Owner) { $dlg.Owner=$Owner } elseif ($script:MainWin) { $dlg.Owner=$script:MainWin } } catch {}   # 未显示过的窗口设 Owner 会抛，设不上就居中屏幕
    $dlg.FindName('Name').Text=[string]$Group.name
    $grid=$dlg.FindName('Steps')
    # 工作副本（不改原组，取消即丢弃）
    $steps=New-Object System.Collections.Generic.List[object]
    foreach ($s in @($Group.steps)) { $steps.Add($s) }
    $refresh={
        $rows=New-Object System.Collections.ObjectModel.ObservableCollection[object]
        foreach ($s in $steps) { $r=New-Object ShRow; $r.T1=(Get-StepKindLabel $s.kind); $r.T2=(Format-StepListSummary $s); $r.T3=(Format-DelayShort ([int]$s.delayMs)); $r.Ref=$s; $rows.Add($r) }
        $grid.ItemsSource=$rows
    }.GetNewClosure()
    & $refresh
    $selIdx={ if($null -eq $grid.SelectedItem){ -1 } else { $steps.IndexOf($grid.SelectedItem.Ref) } }.GetNewClosure()
    # 新增：类型菜单
    $kinds=@(@('启动程序','app'),@('发送按键','keys'),@('发送文本','text'),@('音量','volume'),@('窗口动作','window'),@('系统命令','system'),@('延时','delay'),@('消息','message'))
    $menu=New-DarkContextMenu
    foreach ($k in $kinds) {
        $mi=New-Object System.Windows.Controls.MenuItem; $mi.Header=$k[0]; $mi.Tag=$k[1]
        $mi.Add_Click({ param($s,$e) $n=Show-StepDialogWpf ([string]$s.Tag) $null @() $dlg; if($n){ $i=& $selIdx; $pos=Get-InsertPosition $i $steps.Count; $steps.Insert($pos,$n); & $refresh; $grid.SelectedIndex=$pos } }.GetNewClosure())
        [void]$menu.Items.Add($mi)
    }
    $dlg.FindName('SAdd').Add_Click({ $menu.PlacementTarget=$dlg.FindName('SAdd'); $menu.IsOpen=$true }.GetNewClosure())
    $dlg.FindName('SEdit').Add_Click({ $i=& $selIdx; if($i -ge 0){ $n=Show-StepDialogWpf ([string]$steps[$i].kind) $steps[$i] @() $dlg; if($n){ $steps[$i]=$n; & $refresh; $grid.SelectedIndex=$i } } }.GetNewClosure())
    # 双击条目=编辑（与启动清单/提醒等列表一致）：转触发「编辑」按钮
    $grid.Add_MouseDoubleClick({ if((& $selIdx) -ge 0){ $dlg.FindName('SEdit').RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent))) } }.GetNewClosure())
    $dlg.FindName('SDel').Add_Click({ $i=& $selIdx; if($i -ge 0){ $steps.RemoveAt($i); & $refresh; if($steps.Count -gt 0){ $grid.SelectedIndex=[Math]::Min($i,$steps.Count-1) } } }.GetNewClosure())   # 删除后选中下一条
    $dlg.FindName('SUp').Add_Click({ $i=& $selIdx; if($i -gt 0){ $t=$steps[$i]; $steps[$i]=$steps[$i-1]; $steps[$i-1]=$t; & $refresh; $grid.SelectedIndex=$i-1 } }.GetNewClosure())
    $dlg.FindName('SDown').Add_Click({ $i=& $selIdx; if($i -ge 0 -and $i -lt $steps.Count-1){ $t=$steps[$i]; $steps[$i]=$steps[$i+1]; $steps[$i+1]=$t; & $refresh; $grid.SelectedIndex=$i+1 } }.GetNewClosure())
    $box=@{R=$null}
    $dlg.FindName('Ok').Add_Click({
        if ([string]::IsNullOrWhiteSpace($dlg.FindName('Name').Text)) { [System.Windows.MessageBox]::Show('请填写动作组名称。','Clockwork')|Out-Null; return }
        $ret=New-ActionGroup @{ name=$dlg.FindName('Name').Text; enabled=[bool]$Group.enabled; steps=$steps.ToArray() }; $ret.id=$Group.id   # .ToArray()：@($List[object]) 在 PS5.1 会抛 ArgumentException
        $box.R=$ret; $dlg.DialogResult=$true
    }.GetNewClosure())
    $dlg.FindName('Cancel').Add_Click({ $dlg.DialogResult=$false }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}
