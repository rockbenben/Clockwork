# StartupHelper.WpfDialogs.ps1 —— WPF 对话框 + 提醒弹窗（配合 WpfGui.ps1）
$script:DlgRes = @'
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="Ink" Color="#E6E9ED"/><SolidColorBrush x:Key="Muted" Color="#8B95A1"/><SolidColorBrush x:Key="Signal" Color="#F0651A"/>
  <SolidColorBrush x:Key="Panel" Color="#2E343B"/><SolidColorBrush x:Key="Line" Color="#3C434B"/>
  <Style TargetType="TextBox"><Setter Property="Background" Value="#2E343B"/><Setter Property="Foreground" Value="#E6E9ED"/><Setter Property="BorderBrush" Value="#3C434B"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="6,4"/><Setter Property="FontSize" Value="14"/><Setter Property="CaretBrush" Value="#E6E9ED"/></Style>
  <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#E6E9ED"/><Setter Property="FontSize" Value="14"/><Setter Property="VerticalAlignment" Value="Center"/></Style>
  <Style x:Key="Primary" TargetType="Button"><Setter Property="Foreground" Value="White"/><Setter Property="FontWeight" Value="Bold"/><Setter Property="FontSize" Value="14"/><Setter Property="Height" Value="34"/><Setter Property="MinWidth" Value="88"/><Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#F0651A" CornerRadius="3" Padding="14,0"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#FF7A2A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style x:Key="Ghost" TargetType="Button"><Setter Property="Foreground" Value="#E6E9ED"/><Setter Property="FontSize" Value="14"/><Setter Property="Height" Value="34"/><Setter Property="MinWidth" Value="80"/><Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2E343B" BorderBrush="#3C434B" BorderThickness="1" CornerRadius="3" Padding="14,0"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#F0651A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="#E6E9ED"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="b" Background="Transparent" Padding="8,5"><ContentPresenter/></Border>
      <ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="#3A4149"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="ComboBox"><Setter Property="Foreground" Value="#E6E9ED"/><Setter Property="Background" Value="#2E343B"/><Setter Property="BorderBrush" Value="#3C434B"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
      <Grid>
        <ToggleButton Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
          <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#2E343B" BorderBrush="#3C434B" BorderThickness="1" CornerRadius="3">
            <Path Data="M0,0 L4,4 L8,0 Z" Fill="#8B95A1" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,9,0"/></Border></ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="9,0,26,0" VerticalAlignment="Center" HorizontalAlignment="Left" IsHitTestVisible="False"/>
        <Popup IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Slide">
          <Border Background="#2A3038" BorderBrush="#3C434B" BorderThickness="1" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
            <ScrollViewer MaxHeight="260"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border>
        </Popup>
      </Grid></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
'@

function New-WpfDialog {
    param([string]$Title, [double]$Width=560, $Owner)
    $x = "<Window xmlns=`"http://schemas.microsoft.com/winfx/2006/xaml/presentation`" xmlns:x=`"http://schemas.microsoft.com/winfx/2006/xaml`" Title=`"$Title`" SizeToContent=`"Height`" Width=`"$Width`" WindowStartupLocation=`"CenterOwner`" Background=`"#262B31`" WindowStyle=`"ToolWindow`" ResizeMode=`"NoResize`" FontFamily=`"Microsoft YaHei UI`" TextOptions.TextFormattingMode=`"Display`"><StackPanel x:Name=`"Body`" Margin=`"20`"/></Window>"
    $dlg = [Windows.Markup.XamlReader]::Parse($x)
    $dlg.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse($script:DlgRes))
    # Owner=「从未显示过的窗口」会抛 InvalidOperationException（主窗最小化启动时就是这种状态）——
    # 统一 try/catch：设不上就让对话框自行居中屏幕，不炸。
    try { if ($Owner) { $dlg.Owner = $Owner } elseif ($script:MainWin) { $dlg.Owner = $script:MainWin } } catch {}
    $dlg
}
$script:InkBrush = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#E6E9ED')
$script:MutedBrush = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#8B95A1')
function Add-DlgRow {
    param($Body, [string]$Label, $Control)
    $g = New-Object System.Windows.Controls.Grid; $g.Margin = '0,0,0,12'
    $c0 = New-Object System.Windows.Controls.ColumnDefinition; $c0.Width = '116'
    $c1 = New-Object System.Windows.Controls.ColumnDefinition; $c1.Width = '*'
    $g.ColumnDefinitions.Add($c0); $g.ColumnDefinitions.Add($c1)
    if ($Label) { $t=New-Object System.Windows.Controls.TextBlock; $t.Text=$Label; $t.Foreground=$script:InkBrush; $t.VerticalAlignment='Center'; $t.FontSize=14; [System.Windows.Controls.Grid]::SetColumn($t,0); [void]$g.Children.Add($t) }
    if ($Control) { [System.Windows.Controls.Grid]::SetColumn($Control,1); if(-not $Label){[System.Windows.Controls.Grid]::SetColumnSpan($Control,2)}; [void]$g.Children.Add($Control) }
    [void]$Body.Children.Add($g); $g
}
function New-DlgText { param([string]$Val='') $t=New-Object System.Windows.Controls.TextBox; $t.Text=[string]$Val; $t.Height=30; $t }

# 文件选择：Microsoft.Win32.OpenFileDialog（WPF 原生）。DereferenceLinks=$false 保留 .lnk 本身路径（.lnk 是合法目标）。
function Select-FilePathDialog {
    param([string]$Current)
    $ofd = New-Object Microsoft.Win32.OpenFileDialog
    $ofd.Title = '选择文件'
    $ofd.DereferenceLinks = $false
    $ofd.Filter = '所有文件 (*.*)|*.*|程序 (*.exe)|*.exe|脚本 (*.ps1;*.bat;*.cmd)|*.ps1;*.bat;*.cmd|快捷方式 (*.lnk)|*.lnk'
    try {
        if ($Current) {
            $dir = if (Test-Path $Current -PathType Container) { $Current } else { Split-Path -Parent $Current }
            if ($dir -and (Test-Path $dir)) { $ofd.InitialDirectory = (Resolve-Path $dir).Path }
        }
    } catch {}
    if ($ofd.ShowDialog()) { $ofd.FileName } else { $null }
}

# 文件夹选择：.NET Framework 下只有 WinForms FolderBrowserDialog（程序已加载 WinForms）。
function Select-FolderPathDialog {
    param([string]$Current)
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    $fbd = New-Object System.Windows.Forms.FolderBrowserDialog
    $fbd.Description = '选择工作目录'
    try { if ($Current -and (Test-Path $Current -PathType Container)) { $fbd.SelectedPath = (Resolve-Path $Current).Path } } catch {}
    if ($fbd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $fbd.SelectedPath } else { $null }
}

# 「文本框 + 右侧按钮」行：按钮调 $Picker（收当前文本、返回新值或 $null），非空回填。返回 TextBox（.Row=行 Grid）。
function Add-DlgBrowseRow {
    param($Body, [string]$Label, [string]$Val, [scriptblock]$Picker, [string]$BtnText='浏览…')
    $tb = New-DlgText $Val
    $g = New-Object System.Windows.Controls.Grid
    $c0=New-Object System.Windows.Controls.ColumnDefinition; $c0.Width='*'
    $c1=New-Object System.Windows.Controls.ColumnDefinition; $c1.Width='Auto'
    $g.ColumnDefinitions.Add($c0); $g.ColumnDefinitions.Add($c1)
    [System.Windows.Controls.Grid]::SetColumn($tb,0); [void]$g.Children.Add($tb)
    $btn=New-Object System.Windows.Controls.Button; $btn.Content='…'; $btn.ToolTip=$BtnText; $btn.Style=$Body.FindResource('Ghost'); $btn.Height=30; $btn.MinWidth=36; $btn.Margin='8,0,0,0'   # 图标按钮（省空间，仿资源管理器/快捷方式编辑器的「…」）；文字说明进 ToolTip
    [System.Windows.Controls.Grid]::SetColumn($btn,1); [void]$g.Children.Add($btn)
    $btn.Add_Click({ $r = & $Picker $tb.Text; if ($r) { $tb.Text = [string]$r } }.GetNewClosure())
    $row = Add-DlgRow $Body $Label $g
    $tb | Add-Member -NotePropertyName Row -NotePropertyValue $row -Force
    $tb
}

# 「文本框 + 捕获按钮」行：点捕获→按快捷键→回填 Ctrl+Enter 这类串。再点/失焦取消。纯修饰键忽略、继续等。返回 TextBox（.Row=行 Grid）。
function Add-DlgCaptureRow {
    param($Body, [string]$Label, [string]$Val)
    $tb = New-DlgText $Val
    $g = New-Object System.Windows.Controls.Grid
    $c0=New-Object System.Windows.Controls.ColumnDefinition; $c0.Width='*'
    $c1=New-Object System.Windows.Controls.ColumnDefinition; $c1.Width='Auto'
    $g.ColumnDefinitions.Add($c0); $g.ColumnDefinitions.Add($c1)
    [System.Windows.Controls.Grid]::SetColumn($tb,0); [void]$g.Children.Add($tb)
    $btn=New-Object System.Windows.Controls.Button; $btn.Content='捕获'; $btn.Style=$Body.FindResource('Ghost'); $btn.Height=30; $btn.MinWidth=64; $btn.Margin='8,0,0,0'
    [System.Windows.Controls.Grid]::SetColumn($btn,1); [void]$g.Children.Add($btn)
    $state=@{ cap=$false }
    $enter={ $state.cap=$true; $btn.Content='按下快捷键…'; [void]$btn.Focus() }.GetNewClosure()
    $exit ={ $state.cap=$false; $btn.Content='捕获' }.GetNewClosure()
    $btn.Add_Click({ if($state.cap){ & $exit } else { & $enter } }.GetNewClosure())
    $btn.Add_LostKeyboardFocus({ if($state.cap){ & $exit } }.GetNewClosure())
    $btn.Add_PreviewKeyDown({ param($s,$e)
        if(-not $state.cap){ return }
        $k=$e.Key; if($k -eq [System.Windows.Input.Key]::System){ $k=$e.SystemKey }
        $name=ConvertFrom-WpfKeyName ([string]$k)
        if(-not $name){ $e.Handled=$true; return }
        $mods=@(); $m=[System.Windows.Input.Keyboard]::Modifiers
        if($m -band [System.Windows.Input.ModifierKeys]::Control){ $mods+='Ctrl' }
        if($m -band [System.Windows.Input.ModifierKeys]::Alt){ $mods+='Alt' }
        if($m -band [System.Windows.Input.ModifierKeys]::Shift){ $mods+='Shift' }
        if($m -band [System.Windows.Input.ModifierKeys]::Windows){ $mods+='Win' }
        $tb.Text=(Format-KeyCombo $mods $name)
        $e.Handled=$true; & $exit
    }.GetNewClosure())
    $row = Add-DlgRow $Body $Label $g
    $tb | Add-Member -NotePropertyName Row -NotePropertyValue $row -Force
    $tb
}

# 从当前「有主窗口」的进程里选一个，返回进程名（不含 .exe）。正对「窗口动作」按窗口操作的语义。
# 顶部搜索框：输入进程名/窗口标题即时过滤（不区分大小写）。
function Select-ProcessNameDialog {
    param($Owner)
    $dlg = New-WpfDialog '选择进程' 460 $Owner; $body=$dlg.FindName('Body')
    $tSearch = New-DlgText ''; $tSearch.Margin='0,0,0,4'; [void]$body.Children.Add($tSearch)
    $hs = New-Object System.Windows.Controls.TextBlock; $hs.Text='输入进程名或窗口标题过滤'; $hs.Foreground=$script:MutedBrush; $hs.FontSize=12; $hs.Margin='0,0,0,8'; [void]$body.Children.Add($hs)
    $lb = New-Object System.Windows.Controls.ListBox
    $lb.Height=300; $lb.Background=$dlg.FindResource('Panel'); $lb.Foreground=$script:InkBrush; $lb.BorderBrush=$dlg.FindResource('Line'); $lb.FontSize=13
    [System.Windows.Controls.ScrollViewer]::SetHorizontalScrollBarVisibility($lb,'Disabled')   # 长标题裁到视口内、不出横向滚动条
    [void]$body.Children.Add($lb)   # 直接进 Body（撑满宽度）
    $procs = @(Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle } | Sort-Object ProcessName -Unique | ForEach-Object { [pscustomobject]@{ Name=[string]$_.ProcessName; Title=[string]$_.MainWindowTitle } })
    $fill = {
        $q = $tSearch.Text.Trim()
        $lb.Items.Clear()
        $matched = if ($q) { @($procs | Where-Object { $_.Name.IndexOf($q,[System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or $_.Title.IndexOf($q,[System.StringComparison]::OrdinalIgnoreCase) -ge 0 }) } else { $procs }
        foreach ($p in $matched) {
            $ttl=$p.Title; if($ttl.Length -gt 30){ $ttl=$ttl.Substring(0,30)+'…' }
            $it=New-Object System.Windows.Controls.ListBoxItem; $it.Content="$($p.Name)  —  $ttl"; $it.Tag=$p.Name; $it.Foreground=$script:InkBrush
            [void]$lb.Items.Add($it)
        }
        if ($lb.Items.Count -eq 0) { $it=New-Object System.Windows.Controls.ListBoxItem; $it.Content=$(if($q){'（无匹配）'}else{'（没有检测到带窗口的程序）'}); $it.Foreground=$script:MutedBrush; $it.IsEnabled=$false; [void]$lb.Items.Add($it) }
    }.GetNewClosure()
    & $fill
    $tSearch.Add_TextChanged($fill)
    $dlg.Add_Loaded({ [void]$tSearch.Focus() }.GetNewClosure())   # 打开即聚焦搜索框，可直接输入
    $box=@{ R=$null }
    $lb.Add_MouseDoubleClick({ if($lb.SelectedItem -and $lb.SelectedItem.Tag){ $box.R=[string]$lb.SelectedItem.Tag; $dlg.DialogResult=$true } }.GetNewClosure())
    Add-DlgButtons $dlg $body ({ if($lb.SelectedItem -and $lb.SelectedItem.Tag){ $box.R=[string]$lb.SelectedItem.Tag }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}

# 日历选日期，返回 yyyy-MM-dd。$Current 能解析则预选，否则默认今天。自成小对话框（Calendar 用默认外观，不嵌暗色行内）。
function Select-DateDialog {
    param([string]$Current, $Owner)
    $dlg = New-WpfDialog '选择日期' 300 $Owner; $body=$dlg.FindName('Body')
    $dlg.SizeToContent = 'WidthAndHeight'   # 让窗口贴着日历自适应宽高：高分屏下日历不再被右侧裁掉、也不留大片空白
    $cal = New-Object System.Windows.Controls.Calendar
    $cal.HorizontalAlignment = 'Center'; $cal.Margin = '0,0,0,4'
    $d = [datetime]::MinValue
    if ([datetime]::TryParseExact($Current, 'yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$d)) { $cal.SelectedDate=$d; $cal.DisplayDate=$d }
    [void]$body.Children.Add($cal)   # 直接进 Body 居中，不用 Add-DlgRow（那会多出一列空标签列、把日历挤偏）
    $box=@{ R=$null }
    Add-DlgButtons $dlg $body ({ if($cal.SelectedDate){ $box.R=([datetime]$cal.SelectedDate).ToString('yyyy-MM-dd') }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}

# 步骤通用「执行条件」两行（仅星期 + 仅 N 点前）——所有启动步骤对话框共用；由 Build-LaunchPlan 在开机/重跑时生效。
# 返回 @{Checks;Before;Hour}，OnOk 里用 Get-DlgCondValues 取值。此前仅 app 对话框保留这两字段、其余步骤编辑即丢失。
function Add-DlgCondRows {
    param($Body, $Step)
    $daysPanel=New-Object System.Windows.Controls.StackPanel; $daysPanel.Orientation='Horizontal'
    $checks=New-Object System.Collections.ArrayList; $dn=@('一','二','三','四','五','六','日'); $cur=@(@($Step.days)|ForEach-Object{[int]$_})
    for($i=1;$i -le 7;$i++){ $c=New-Object System.Windows.Controls.CheckBox; $c.Content=$dn[$i-1]; $c.Foreground=$script:InkBrush; $c.Margin='0,0,10,0'; $c.IsChecked=($cur -contains $i); $c.Tag=$i; [void]$checks.Add($c); [void]$daysPanel.Children.Add($c) }
    $hd=New-Object System.Windows.Controls.TextBlock; $hd.Text='（都不选=每天）'; $hd.Foreground=$script:MutedBrush; $hd.VerticalAlignment='Center'; $hd.FontSize=12; [void]$daysPanel.Children.Add($hd)
    Add-DlgRow $Body '仅星期' $daysPanel | Out-Null
    $sp=New-Object System.Windows.Controls.StackPanel; $sp.Orientation='Horizontal'
    $cB=New-Object System.Windows.Controls.CheckBox; $cB.Content='仅当天'; $cB.Foreground=$script:InkBrush; $cB.IsChecked=[bool]$Step.onlyBefore8; $cB.VerticalAlignment='Center'
    $tH=New-DlgText ([string](Get-BeforeHour $Step)); $tH.Width=46; $tH.Margin='6,0,4,0'
    $lbB=New-Object System.Windows.Controls.TextBlock; $lbB.Text='点前才执行（过点就跳过本步）'; $lbB.Foreground=$script:MutedBrush; $lbB.VerticalAlignment='Center'; $lbB.FontSize=12
    [void]$sp.Children.Add($cB); [void]$sp.Children.Add($tH); [void]$sp.Children.Add($lbB)
    Add-DlgRow $Body '时段' $sp | Out-Null
    @{ Checks=$checks; Before=$cB; Hour=$tH }
}
function Get-DlgCondValues {
    param($C)
    $h=8;[void][int]::TryParse($C.Hour.Text,[ref]$h); if($h -lt 1 -or $h -gt 23){$h=8}
    @{ days=@($C.Checks|Where-Object{$_.IsChecked}|ForEach-Object{[int]$_.Tag}); onlyBefore8=[bool]$C.Before.IsChecked; beforeHour=$h }
}
# 「用途说明」行，所有步骤对话框共用。返回 @{Note=<TextBox>}，OnOk 里取 .Note.Text。
# （图标 emoji 已按需求整体移除：界面、显示、模型字段都清掉了；函数名沿用旧名以免改 9 处调用点。）
function Add-DlgIconNoteRows {
    param($Body, $Step)
    $tNote=New-DlgText ([string]$Step.note); Add-DlgRow $Body '说明' $tNote | Out-Null
    @{ Note=$tNote }
}
function Add-DlgButtons {
    param($Dlg, $Body, [scriptblock]$OnOk)
    $bp=New-Object System.Windows.Controls.StackPanel; $bp.Orientation='Horizontal'; $bp.HorizontalAlignment='Right'; $bp.Margin='0,8,0,0'
    $ok=New-Object System.Windows.Controls.Button; $ok.Content='确定'; $ok.Style=$Dlg.FindResource('Primary'); $ok.Margin='0,0,10,0'
    $cn=New-Object System.Windows.Controls.Button; $cn.Content='取消'; $cn.Style=$Dlg.FindResource('Ghost')
    [void]$bp.Children.Add($ok); [void]$bp.Children.Add($cn); [void]$Body.Children.Add($bp)
    $ok.Add_Click({ if (& $OnOk) { $Dlg.DialogResult=$true } }.GetNewClosure())
    $cn.Add_Click({ $Dlg.DialogResult=$false }.GetNewClosure())
}

function Show-LaunchItemDialogWpf {
    param($Step, $Owner)
    if ($null -eq $Step) { $Step = New-LaunchStep 'app' }
    $dlg = New-WpfDialog '编辑 · 启动程序' 580 $Owner; $body=$dlg.FindName('Body')
    $tName=New-DlgText $Step.label; Add-DlgRow $body '标签' $tName | Out-Null
    $tTgt =Add-DlgBrowseRow $body '目标' $Step.target { param($cur) Select-FilePathDialog $cur }
    $hT=New-Object System.Windows.Controls.TextBlock; $hT.Text='程序 / 文档 / 快捷方式 / 网址均可；.ps1 会自动用 PowerShell 运行'; $hT.Foreground=$script:MutedBrush; $hT.FontSize=12; $hT.TextWrapping='Wrap'; Add-DlgRow $body $null $hT | Out-Null
    $tAlt =New-Object System.Windows.Controls.TextBox; $tAlt.Text=[string]$Step.altTargets; $tAlt.AcceptsReturn=$true; $tAlt.TextWrapping='Wrap'; $tAlt.Height=52; $tAlt.VerticalScrollBarVisibility='Auto'; Add-DlgRow $body '备用路径' $tAlt | Out-Null
    $hAlt=New-Object System.Windows.Controls.TextBlock; $hAlt.Text='可选，每行一条完整路径。「目标」是完整路径且不存在时，按顺序用第一个存在的备用路径（多设备安装路径不一致时用）。'; $hAlt.Foreground=$script:MutedBrush; $hAlt.FontSize=12; $hAlt.TextWrapping='Wrap'; Add-DlgRow $body $null $hAlt | Out-Null
    $tArg =New-DlgText $Step.args; Add-DlgRow $body '参数' $tArg | Out-Null
    $tDir =Add-DlgBrowseRow $body '工作目录' $Step.workDir { param($cur) Select-FolderPathDialog ($(if([string]::IsNullOrWhiteSpace($cur)){ try{ Split-Path -Parent $tTgt.Text }catch{''} }else{$cur})) }
    $hD=New-Object System.Windows.Controls.TextBlock; $hD.Text='留空 = 用「目标」所在目录'; $hD.Foreground=$script:MutedBrush; $hD.FontSize=12; $hD.TextWrapping='Wrap'; Add-DlgRow $body $null $hD | Out-Null
    $cEle =New-Object System.Windows.Controls.CheckBox; $cEle.Content='管理员权限'; $cEle.Foreground=$script:InkBrush; $cEle.IsChecked=[bool]$Step.elevated; Add-DlgRow $body $null $cEle | Out-Null
    $cAct =New-Object System.Windows.Controls.CheckBox; $cAct.Content='已在运行则激活窗口（不重复启动）'; $cAct.Foreground=$script:InkBrush; $cAct.IsChecked=[bool]$Step.activateIfRunning; Add-DlgRow $body $null $cAct | Out-Null
    $tAct =Add-DlgBrowseRow $body '进程名' $Step.activateProcess { param($cur) Select-ProcessNameDialog $dlg } '选择…'; $rowAct=$tAct.Row
    $hAct=New-Object System.Windows.Controls.TextBlock; $hAct.Text='留空 = 用目标程序名自动判断；启动器会开另一个进程名时在此手填（不含 .exe）'; $hAct.Foreground=$script:MutedBrush; $hAct.FontSize=12; $hAct.TextWrapping='Wrap'; $rowActHint=Add-DlgRow $body $null $hAct
    $togAct={ $v=if($cAct.IsChecked){'Visible'}else{'Collapsed'}; $rowAct.Visibility=$v; $rowActHint.Visibility=$v }.GetNewClosure()
    $cAct.Add_Checked($togAct); $cAct.Add_Unchecked($togAct); & $togAct
    $cbWs=New-DlgCombo @('正常','最小化','最大化','隐藏') @('','minimized','maximized','hidden') $Step.windowStyle 140; Add-DlgRow $body '窗口风格' $cbWs | Out-Null
    $hWs=New-Object System.Windows.Controls.TextBlock; $hWs.Text='是否生效取决于目标程序；勾了「管理员权限」时窗口风格可能不生效（走系统提权路径）。'; $hWs.Foreground=$script:MutedBrush; $hWs.FontSize=12; $hWs.TextWrapping='Wrap'; Add-DlgRow $body $null $hWs | Out-Null
    $inm=Add-DlgIconNoteRows $body $Step
    $tDly =New-DlgText ([string][int]$Step.delayMs); $tDly.Width=110; $tDly.HorizontalAlignment='Left'; Add-DlgRow $body '执行后延时(ms)' $tDly | Out-Null
    $cond=Add-DlgCondRows $body $Step
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({ $d=0;[void][int]::TryParse($tDly.Text,[ref]$d); $cv=Get-DlgCondValues $cond; $box.R=New-LaunchStep 'app' @{ enabled=[bool]$Step.enabled; label=$tName.Text; target=$tTgt.Text; args=$tArg.Text; workDir=$tDir.Text; elevated=[bool]$cEle.IsChecked; activateIfRunning=[bool]$cAct.IsChecked; activateProcess=$tAct.Text; windowStyle=(Get-ComboValue $cbWs); altTargets=$tAlt.Text; delayMs=$d; days=$cv.days; onlyBefore8=$cv.onlyBefore8; beforeHour=$cv.beforeHour; note=$inm.Note.Text }; $true }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}

function Show-ReminderDialogWpf {
    param($R, $Groups, $Owner)
    if ($null -eq $R) { $R = New-Reminder }
    $dlg = New-WpfDialog '编辑 · 提醒' 760 $Owner; $body=$dlg.FindName('Body')
    $cbTrig=New-DlgCombo @('定时（到点提醒）','登录时') @('time','startup') $R.trigger 210; Add-DlgRow $body '触发' $cbTrig | Out-Null
    $tTime=New-DlgText $R.time; $tTime.Width=100; $tTime.HorizontalAlignment='Left'; $rowTime=Add-DlgRow $body '时间(HH:mm)' $tTime
    $spS=New-Object System.Windows.Controls.StackPanel; $spS.Orientation='Horizontal'
    $cbSMode=New-DlgCombo @('总是','仅 N 点前','仅 N 点后') @('any','before','after') $R.startupHourMode 130
    $tSHour=New-DlgText ([string][int]$R.startupHour); $tSHour.Width=54; $tSHour.Margin='8,0,4,0'
    $lbH=New-Object System.Windows.Controls.TextBlock; $lbH.Text='点；'; $lbH.Foreground=$script:InkBrush; $lbH.VerticalAlignment='Center'
    $lbH2=New-Object System.Windows.Controls.TextBlock; $lbH2.Text='仅开机'; $lbH2.Foreground=$script:InkBrush; $lbH2.VerticalAlignment='Center'
    $tSWin=New-DlgText ([string][int]$R.startupWithinMinutes); $tSWin.Width=54; $tSWin.Margin='6,0,4,0'
    $lbW=New-Object System.Windows.Controls.TextBlock; $lbW.Text='分钟内打开才算「开机登录」、才提醒（填 0 = 每次打开本程序都提醒）'; $lbW.Foreground=$script:MutedBrush; $lbW.VerticalAlignment='Center'; $lbW.FontSize=12; $lbW.TextWrapping='Wrap'; $lbW.MaxWidth=300
    [void]$spS.Children.Add($cbSMode); [void]$spS.Children.Add($tSHour); [void]$spS.Children.Add($lbH); [void]$spS.Children.Add($lbH2); [void]$spS.Children.Add($tSWin); [void]$spS.Children.Add($lbW)
    $rowSMode=Add-DlgRow $body '登录时机' $spS
    # 小时框仅在「仅 N 点前/后」时显示（「总是」用不到，摆着让人困惑）
    $togSH={ $v = if((Get-ComboValue $cbSMode) -eq 'any'){'Collapsed'}else{'Visible'}; $tSHour.Visibility=$v; $lbH.Visibility=$v }.GetNewClosure()
    $cbSMode.Add_SelectionChanged($togSH); & $togSH
    $cbRec=New-DlgCombo @('按星期','每 N 天','每月某号') @('daily','everyNDays','monthly') $R.recurType 160; Add-DlgRow $body '周期' $cbRec | Out-Null
    $daysPanel=New-Object System.Windows.Controls.StackPanel; $daysPanel.Orientation='Horizontal'
    $dayChecks=New-Object System.Collections.ArrayList; $dn=@('一','二','三','四','五','六','日'); $cur=@(@($R.days)|ForEach-Object{[int]$_})
    for($i=1;$i -le 7;$i++){ $c=New-Object System.Windows.Controls.CheckBox; $c.Content=$dn[$i-1]; $c.Foreground=$script:InkBrush; $c.Margin='0,0,12,0'; $c.IsChecked=($cur -contains $i); $c.Tag=$i; [void]$dayChecks.Add($c); [void]$daysPanel.Children.Add($c) }
    $hd=New-Object System.Windows.Controls.TextBlock; $hd.Text='（都不选=每天）'; $hd.Foreground=$script:MutedBrush; $hd.VerticalAlignment='Center'; $hd.FontSize=12; [void]$daysPanel.Children.Add($hd)
    $rowDays=Add-DlgRow $body '星期' $daysPanel
    $spInt=New-Object System.Windows.Controls.StackPanel; $spInt.Orientation='Horizontal'
    $tInt=New-DlgText ([string][int]$R.intervalDays); $tInt.Width=60
    $lbAn=New-Object System.Windows.Controls.TextBlock; $lbAn.Text='天；起算日'; $lbAn.Foreground=$script:InkBrush; $lbAn.VerticalAlignment='Center'; $lbAn.Margin='6,0,6,0'
    $tAnchor=New-DlgText ([string]$R.anchorDate); $tAnchor.Width=110
    $lbAn2=New-Object System.Windows.Controls.TextBlock; $lbAn2.Text='（yyyy-MM-dd；留空=保存时自动填今天）'; $lbAn2.Foreground=$script:MutedBrush; $lbAn2.VerticalAlignment='Center'; $lbAn2.FontSize=12; $lbAn2.Margin='6,0,0,0'
    $btnDate=New-Object System.Windows.Controls.Button; $btnDate.Content='…'; $btnDate.ToolTip='选日期'; $btnDate.Style=$dlg.FindResource('Ghost'); $btnDate.Height=30; $btnDate.MinWidth=36; $btnDate.Margin='6,0,0,0'
    $btnDate.Add_Click({ $r=Select-DateDialog $tAnchor.Text $dlg; if($r){ $tAnchor.Text=[string]$r } }.GetNewClosure())
    [void]$spInt.Children.Add($tInt); [void]$spInt.Children.Add($lbAn); [void]$spInt.Children.Add($tAnchor); [void]$spInt.Children.Add($btnDate); [void]$spInt.Children.Add($lbAn2)
    $rowInt=Add-DlgRow $body '每几天' $spInt
    $tMon=New-DlgText ([string][int]$R.monthlyDay); $tMon.Width=60; $tMon.HorizontalAlignment='Left'; $rowMon=Add-DlgRow $body '每月几号' $tMon
    $tMsg=New-Object System.Windows.Controls.TextBox; $tMsg.Text=[string]$R.message; $tMsg.AcceptsReturn=$true; $tMsg.TextWrapping='Wrap'; $tMsg.Height=80; $tMsg.VerticalScrollBarVisibility='Auto'; Add-DlgRow $body '文本' $tMsg | Out-Null
    $cSpk=New-Object System.Windows.Controls.CheckBox; $cSpk.Content='语音播报'; $cSpk.Foreground=$script:InkBrush; $cSpk.IsChecked=[bool]$R.speak; Add-DlgRow $body $null $cSpk | Out-Null
    # 「弹确认框」勾选已移除：是/否 只跟「点是后」走（配了动作=弹是/否问你，否则只有「确定」），单独的勾选框徒增困惑。
    $gs2=@($Groups); $gl2=@('（不使用）')+@($gs2|ForEach-Object{[string]$_.name}); $gv2=@('')+@($gs2|ForEach-Object{[string]$_.id})
    $cbSilent=New-DlgCombo $gl2 $gv2 $R.silentGroupId 240; Add-DlgRow $body '静默动作组' $cbSilent | Out-Null
    $hs=New-Object System.Windows.Controls.TextBlock; $hs.Text='选了则到点不弹窗，直接静默运行该动作组（忽略文本 / 语音 / 点是后）'; $hs.Foreground=$script:MutedBrush; $hs.FontSize=12; $hs.TextWrapping='Wrap'; Add-DlgRow $body $null $hs | Out-Null
    # 「放音乐」类型已并入「运行 / 打开文件」（实现本就相同）；旧配置的 sound 映射成 run 预选，保存后自然升级。
    $yTypeSel = if ([string]$R.onYes.type -eq 'sound') { 'run' } else { [string]$R.onYes.type }
    $cbY=New-DlgCombo @('无','运行 / 打开文件','开网页','运行动作组') @('none','run','url','group') $yTypeSel 130
    $tY=New-DlgText $R.onYes.target
    $cbYG=New-DlgCombo $gl2 $gv2 $(if([string]$R.onYes.type -eq 'group'){[string]$R.onYes.target}else{''}) 220
    $rowYG=New-Object System.Windows.Controls.Grid; $rowYG.Margin='0,0,0,12'
    foreach($w in @('116','130','*')){ $cd=New-Object System.Windows.Controls.ColumnDefinition; $cd.Width=$w; $rowYG.ColumnDefinitions.Add($cd) }
    $lbY=New-Object System.Windows.Controls.TextBlock; $lbY.Text='点是后'; $lbY.Foreground=$script:InkBrush; $lbY.VerticalAlignment='Center'; $lbY.FontSize=14; [System.Windows.Controls.Grid]::SetColumn($lbY,0); [void]$rowYG.Children.Add($lbY)
    [System.Windows.Controls.Grid]::SetColumn($cbY,1); [void]$rowYG.Children.Add($cbY)
    $tYWrap=New-Object System.Windows.Controls.Grid; $tYWrap.Margin='8,0,0,0'
    $wc0=New-Object System.Windows.Controls.ColumnDefinition; $wc0.Width='*'; $wc1=New-Object System.Windows.Controls.ColumnDefinition; $wc1.Width='Auto'
    $tYWrap.ColumnDefinitions.Add($wc0); $tYWrap.ColumnDefinitions.Add($wc1)
    [System.Windows.Controls.Grid]::SetColumn($tY,0); [void]$tYWrap.Children.Add($tY)
    $btnYB=New-Object System.Windows.Controls.Button; $btnYB.Content='…'; $btnYB.ToolTip='浏览…'; $btnYB.Style=$dlg.FindResource('Ghost'); $btnYB.Height=30; $btnYB.MinWidth=36; $btnYB.Margin='8,0,0,0'
    [System.Windows.Controls.Grid]::SetColumn($btnYB,1); [void]$tYWrap.Children.Add($btnYB)
    $btnYB.Add_Click({ $r=Select-FilePathDialog $tY.Text; if($r){ $tY.Text=[string]$r } }.GetNewClosure())
    [System.Windows.Controls.Grid]::SetColumn($tYWrap,2); [void]$rowYG.Children.Add($tYWrap)
    $cbYG.Margin='8,0,0,0'; $cbYG.HorizontalAlignment='Left'; [System.Windows.Controls.Grid]::SetColumn($cbYG,2); [void]$rowYG.Children.Add($cbYG)
    [void]$body.Children.Add($rowYG)
    $togY={ $v=(Get-ComboValue $cbY); if($v -eq 'group'){ $tYWrap.Visibility='Collapsed'; $cbYG.Visibility='Visible' } else { $tYWrap.Visibility='Visible'; $cbYG.Visibility='Collapsed'; $btnYB.Visibility=$(if($v -eq 'run'){'Visible'}else{'Collapsed'}) } }.GetNewClosure()
    $cbY.Add_SelectionChanged($togY); & $togY
    # 无操作自动关闭：到秒数没人点就自动关（重复型提醒会按「重复每N分」继续催；普通提醒当天不再弹）
    $spAuto=New-Object System.Windows.Controls.StackPanel; $spAuto.Orientation='Horizontal'
    $tAuto=New-DlgText ([string][int]$R.popupTimeoutSeconds); $tAuto.Width=60
    $lbA=New-Object System.Windows.Controls.TextBlock; $lbA.Text='秒没人点就自动关掉（填 0 = 不关、一直等你点；重复提醒默认 60 秒）。没配「点是后」的提醒改用系统通知显示，填 20 秒以上会停留久一点'; $lbA.Foreground=$script:MutedBrush; $lbA.VerticalAlignment='Center'; $lbA.FontSize=12; $lbA.Margin='8,0,0,0'; $lbA.TextWrapping='Wrap'; $lbA.MaxWidth=490
    [void]$spAuto.Children.Add($tAuto); [void]$spAuto.Children.Add($lbA)
    Add-DlgRow $body '自动关闭' $spAuto | Out-Null
    # 重复催促：没确认（超时/点稍后除外）就每 N 分钟再弹，直到 HH:mm（留空=不设截止；最多 20 次）
    $spRep=New-Object System.Windows.Controls.StackPanel; $spRep.Orientation='Horizontal'
    $tRep=New-DlgText ([string][int]$R.repeatMinutes); $tRep.Width=60
    $lbR1=New-Object System.Windows.Controls.TextBlock; $lbR1.Text='分钟重复一次（0 = 不重复），直到'; $lbR1.Foreground=$script:InkBrush; $lbR1.VerticalAlignment='Center'; $lbR1.Margin='6,0,6,0'
    $tRepU=New-DlgText ([string]$R.repeatUntil); $tRepU.Width=70
    $lbR2=New-Object System.Windows.Controls.TextBlock; $lbR2.Text='（HH:mm；留空=不限。没点按钮才会重复）'; $lbR2.Foreground=$script:MutedBrush; $lbR2.VerticalAlignment='Center'; $lbR2.FontSize=12; $lbR2.Margin='6,0,0,0'; $lbR2.TextWrapping='Wrap'; $lbR2.MaxWidth=320
    [void]$spRep.Children.Add($tRep); [void]$spRep.Children.Add($lbR1); [void]$spRep.Children.Add($tRepU); [void]$spRep.Children.Add($lbR2)
    Add-DlgRow $body '重复催促' $spRep | Out-Null
    # 到点后延迟：固定 + 随机（错峰/防呆板）；宽限：错过时点（如刚好关机）N 分钟内仍补弹
    $spDly=New-Object System.Windows.Controls.StackPanel; $spDly.Orientation='Horizontal'
    $tDlyS=New-DlgText ([string][int]$R.delaySeconds); $tDlyS.Width=60
    $lbD1=New-Object System.Windows.Controls.TextBlock; $lbD1.Text='秒，再随机加 0 ~'; $lbD1.Foreground=$script:InkBrush; $lbD1.VerticalAlignment='Center'; $lbD1.Margin='6,0,6,0'
    $tRnd=New-DlgText ([string][int]$R.randomDelaySeconds); $tRnd.Width=60
    $lbD2=New-Object System.Windows.Controls.TextBlock; $lbD2.Text='秒'; $lbD2.Foreground=$script:InkBrush; $lbD2.VerticalAlignment='Center'; $lbD2.Margin='6,0,0,0'
    [void]$spDly.Children.Add($tDlyS); [void]$spDly.Children.Add($lbD1); [void]$spDly.Children.Add($tRnd); [void]$spDly.Children.Add($lbD2)
    Add-DlgRow $body '触发后延迟' $spDly | Out-Null
    $spGr=New-Object System.Windows.Controls.StackPanel; $spGr.Orientation='Horizontal'
    $tGr=New-DlgText ([string][int]$R.graceMinutes); $tGr.Width=60
    $lbG=New-Object System.Windows.Controls.TextBlock; $lbG.Text='分钟内错过时点仍补弹（如到点时电脑正关机/睡眠）'; $lbG.Foreground=$script:MutedBrush; $lbG.VerticalAlignment='Center'; $lbG.FontSize=12; $lbG.Margin='8,0,0,0'
    [void]$spGr.Children.Add($tGr); [void]$spGr.Children.Add($lbG)
    $rowGr=Add-DlgRow $body '宽限' $spGr
    # 「登录时」触发没有固定时点：时间/宽限 两行都不适用，一并隐藏
    $togT={ $t=(Get-ComboValue $cbTrig); $v=$(if($t -eq 'time'){'Visible'}else{'Collapsed'}); $rowTime.Visibility=$v; $rowGr.Visibility=$v; $rowSMode.Visibility=$(if($t -eq 'startup'){'Visible'}else{'Collapsed'}) }.GetNewClosure()
    $cbTrig.Add_SelectionChanged($togT); & $togT
    $togR={ $r=(Get-ComboValue $cbRec); $rowDays.Visibility=$(if($r -eq 'daily'){'Visible'}else{'Collapsed'}); $rowInt.Visibility=$(if($r -eq 'everyNDays'){'Visible'}else{'Collapsed'}); $rowMon.Visibility=$(if($r -eq 'monthly'){'Visible'}else{'Collapsed'}) }.GetNewClosure()
    $cbRec.Add_SelectionChanged($togR); & $togR
    $box=@{R=$null}
    Add-DlgButtons $dlg $body ({
        $trig=(Get-ComboValue $cbTrig)
        if ($trig -eq 'time' -and $tTime.Text -notmatch '^([01]\d|2[0-3]):[0-5]\d$') { [System.Windows.MessageBox]::Show('时间格式应为 HH:mm','开机助手')|Out-Null; return $false }
        if ($tRepU.Text.Trim() -ne '' -and $tRepU.Text.Trim() -notmatch '^([01]\d|2[0-3]):[0-5]\d$') { [System.Windows.MessageBox]::Show('「重复直到」格式应为 HH:mm（或留空）','开机助手')|Out-Null; return $false }
        if ($tAnchor.Text.Trim() -ne '' -and $tAnchor.Text.Trim() -notmatch '^\d{4}-\d{2}-\d{2}$') { [System.Windows.MessageBox]::Show('起算日格式应为 yyyy-MM-dd（或留空）','开机助手')|Out-Null; return $false }
        $days=@($dayChecks | Where-Object { $_.IsChecked } | ForEach-Object { [int]$_.Tag })
        # TryParse 失败会把 [ref] 写成 0（而非保留预置值）——清空/误填的框必须显式回退默认，否则
        # startupHour=0 让「仅 N 点前」永假、graceMinutes=0 丢补弹等，全是静默失效。
        $iv=0; if(-not [int]::TryParse($tInt.Text,[ref]$iv)   -or $iv -lt 1){ $iv=1 }
        $md=0; if(-not [int]::TryParse($tMon.Text,[ref]$md)   -or $md -lt 1){ $md=1 }
        $sh=0; if(-not [int]::TryParse($tSHour.Text,[ref]$sh) -or $sh -lt 0 -or $sh -gt 23){ $sh=9 }
        $sw=0; if(-not [int]::TryParse($tSWin.Text,[ref]$sw)  -or $sw -lt 0){ $sw=10 }
        $au=0; if(-not [int]::TryParse($tAuto.Text,[ref]$au)  -or $au -lt 0){ $au=0 }
        $rm=0; if(-not [int]::TryParse($tRep.Text,[ref]$rm)   -or $rm -lt 0){ $rm=0 }
        $ds=0; if(-not [int]::TryParse($tDlyS.Text,[ref]$ds)  -or $ds -lt 0){ $ds=0 }
        $rd=0; if(-not [int]::TryParse($tRnd.Text,[ref]$rd)   -or $rd -lt 0){ $rd=0 }
        $gm=0; if(-not [int]::TryParse($tGr.Text,[ref]$gm)    -or $gm -lt 0){ $gm=5 }
        # 「每 N 天」起算日留空 → 自动填今天：anchorDate 为空时周期判定放行每一天（等于没有间隔），必须有锚点才按 N 天取模
        $anch=$tAnchor.Text.Trim(); if((Get-ComboValue $cbRec) -eq 'everyNDays' -and $anch -eq ''){ $anch=(Get-Date).ToString('yyyy-MM-dd') }
        $box.R = New-Reminder @{
            trigger=$trig; time=(Format-TimeHHmm $tTime.Text); days=$days; recurType=(Get-ComboValue $cbRec); intervalDays=$iv; monthlyDay=$md
            startupHourMode=(Get-ComboValue $cbSMode); startupHour=$sh; startupWithinMinutes=$sw
            message=$tMsg.Text; speak=[bool]$cSpk.IsChecked; onYes=@{ type=(Get-ComboValue $cbY); target=$(if((Get-ComboValue $cbY) -eq 'group'){Get-ComboValue $cbYG}else{$tY.Text}) }
            graceMinutes=$gm; delaySeconds=$ds; randomDelaySeconds=$rd; repeatMinutes=$rm; repeatUntil=(Format-TimeHHmm $tRepU.Text); anchorDate=$anch; popupTimeoutSeconds=$au; silentGroupId=(Get-ComboValue $cbSilent)
        }
        $true
    }.GetNewClosure())
    if ($dlg.ShowDialog()) { $box.R } else { $null }
}

function Show-ActionGroupDialogWpf { param($G,$Owner) [System.Windows.MessageBox]::Show('动作组编辑对话框正在迁移到 WPF（下一步）。','开机助手')|Out-Null; $null }

# 原生系统通知（Win10/11 Toast，即「日常通知」样式）：归属名来自我们注册的 AUMID DisplayName，
# 不会出现 NotifyIcon 气泡那种乱码回退名；不置顶抢视线，错过自动进通知中心。失败返回 $false（调用方退回气泡/弹窗）。
function Show-SystemToast {
    # $Long：横幅时长 短(默认,~5-7s)/长(~25s)。Windows 只有这两档，无法任意秒数（见 Test-ReminderToastLong）。
    param([string]$Message, [string]$Title = '', [bool]$Long = $false)
    try {
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime]
        $tEsc = [System.Security.SecurityElement]::Escape([string]$Title)
        $mEsc = [System.Security.SecurityElement]::Escape([string]$Message)
        $lines = if ([string]::IsNullOrWhiteSpace($Title)) { "<text>$mEsc</text>" } else { "<text>$tEsc</text><text>$mEsc</text>" }
        $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier((Get-AppAumid))
        # 用户在系统设置里关掉了本应用/全局通知时，Show() 是静默 no-op（不抛错）——必须查 Setting，
        # 否则「发成功」是假象、提醒凭空消失；返回 $false 让调用方退回自绘弹窗/气泡。
        if ($notifier.Setting -ne [Windows.UI.Notifications.NotificationSetting]::Enabled) { return $false }
        $dur = if ($Long) { " duration='long'" } else { '' }
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml("<toast$dur><visual><binding template='ToastGeneric'>$lines</binding></visual></toast>")
        $toast = New-Object Windows.UI.Notifications.ToastNotification($xml)
        $notifier.Show($toast)
        $true
    } catch { $false }
}

# 托盘类通知统一先走系统 Toast（归属名/样式正确）；Toast 不可用（老系统/策略禁用）才退回 NotifyIcon 气泡。
function Show-TrayNotify {
    param($Tray, [string]$Title, [string]$Message)
    if (Show-SystemToast $Message $Title) { return }
    try { $Tray.ShowBalloonTip(3000, $Title, $Message, [System.Windows.Forms.ToolTipIcon]::Info) } catch {}
}

# —— 提醒弹窗（WPF，替代 WinForms 版；Actions.ps1 的 Invoke-Reminder 调用它）——
function Show-ReminderPopup {
    param([string]$Message, [bool]$Confirm, [int]$AutoDismissSeconds = 0, [switch]$NoSnooze)
    $box = @{ result = $(if ($Confirm) { 'no' } else { 'ok' }); snoozeMinutes = $null }
    $x = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
  Title="开机助手 · 提醒" SizeToContent="Height" Width="460" WindowStartupLocation="Manual" Background="#2E343B"
  WindowStyle="ToolWindow" ResizeMode="NoResize" Topmost="True" ShowInTaskbar="False" FontFamily="Microsoft YaHei UI" TextOptions.TextFormattingMode="Display">
  <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="5"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
    <Rectangle Grid.Column="0" Fill="#F0651A"/>
    <StackPanel Grid.Column="1" Margin="20,16,20,16">
      <TextBlock Text="提醒" Foreground="#F0651A" FontFamily="Consolas" FontWeight="Bold" FontSize="13"/>
      <TextBlock x:Name="Msg" Foreground="#E6E9ED" FontSize="15" TextWrapping="Wrap" Margin="0,10,0,18"/>
      <StackPanel x:Name="Btns" Orientation="Horizontal" HorizontalAlignment="Right"/>
    </StackPanel>
  </Grid>
</Window>
'@
    $dlg = [Windows.Markup.XamlReader]::Parse($x)
    $dlg.FindName('Msg').Text = $Message
    $btns = $dlg.FindName('Btns')
    $prim = [Windows.Markup.XamlReader]::Parse('<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button"><Setter Property="Foreground" Value="White"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border Background="#F0651A" CornerRadius="3" Padding="14,7"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></ControlTemplate></Setter.Value></Setter></Style>')
    $ghost= [Windows.Markup.XamlReader]::Parse('<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button"><Setter Property="Foreground" Value="#E6E9ED"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border Background="#262B31" BorderBrush="#3C434B" BorderThickness="1" CornerRadius="3" Padding="14,7"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></ControlTemplate></Setter.Value></Setter></Style>')
    # 按钮工厂只负责外观；Add_Click 闭包必须在【函数直层】创建——嵌套在工厂脚本块里 .GetNewClosure()
    # 捕获不到外层的 $box/$dlg（嵌套闭包坑），点击时抛「找不到属性 result」。
    $mk = { param($txt,$st) $b=New-Object System.Windows.Controls.Button; $b.Content=$txt; $b.MinWidth=76; $b.Margin='10,0,0,0'; $b.Cursor='Hand'; $b.Style=$st; $b }
    if ($Confirm) {
        $bY = & $mk '是' $prim;  $bY.Add_Click({ $box.result='yes'; $dlg.Close() }.GetNewClosure()); [void]$btns.Children.Add($bY)
        $bN = & $mk '否' $ghost; $bN.Add_Click({ $box.result='no';  $dlg.Close() }.GetNewClosure()); [void]$btns.Children.Add($bN)
    } else {
        $bO = & $mk '确定' $prim; $bO.Add_Click({ $box.result='ok'; $dlg.Close() }.GetNewClosure()); [void]$btns.Children.Add($bO)
    }
    if (-not $NoSnooze) {
        $bs=New-Object System.Windows.Controls.Button; $bs.Content='稍后…'; $bs.MinWidth=76; $bs.Margin='10,0,0,0'; $bs.Cursor='Hand'; $bs.Style=$ghost
        # 先停自动关闭计时器：InputBox 是模态消息泵，会让 DispatcherTimer 的 tick 照跑，否则用户在
        # 「稍后」输入框里停留超过自动关闭秒数时，提醒弹窗会被 tick 从背后 Close 掉、这次打盹被当成自动关闭丢失。
        $bs.Add_Click({ if($box.timer){ $box.timer.Stop() }; try{Add-Type -AssemblyName Microsoft.VisualBasic}catch{}; $raw=[Microsoft.VisualBasic.Interaction]::InputBox('几分钟后再提醒？可填分钟数或 1h20m','稍后提醒','10'); if([string]::IsNullOrWhiteSpace($raw)){ if($box.timer){ $box.timer.Start() }; return }; $m=ConvertFrom-DurationText $raw; $box.snoozeMinutes= if($m -ge 1){[int]$m}else{10}; $box.result='snooze'; $dlg.Close() }.GetNewClosure())   # 取消/清空=反悔：重开自动关闭计时器、留在提醒弹窗，不强制打盹
        [void]$btns.Children.Add($bs)
    }
    $dlg.Add_ContentRendered({ $wa=[System.Windows.SystemParameters]::WorkArea; $dlg.Left=$wa.Right-$dlg.ActualWidth-16; $dlg.Top=$wa.Bottom-$dlg.ActualHeight-16 }.GetNewClosure())
    if ($AutoDismissSeconds -gt 0) { $t=New-Object System.Windows.Threading.DispatcherTimer; $t.Interval=[TimeSpan]::FromSeconds($AutoDismissSeconds); $t.Add_Tick({ $t.Stop(); $box.result=''; $dlg.Close() }.GetNewClosure()); $box.timer=$t; $t.Start() }
    [void]$dlg.ShowDialog()
    [pscustomobject]@{ Action=$box.result; SnoozeMinutes=$box.snoozeMinutes }
}
