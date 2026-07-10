# StartupHelper.WpfGui.ps1 —— WPF 版界面（替代 WinForms 版 Gui.ps1）。逻辑层 Core/Actions/SystemStartup/Win32 复用。
# WPF 天生 DPI 自适应 + 完整可主题化：暗色/扁平/无原生描边，不再跟原生控件缠斗。
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Add-Type -AssemblyName System.Windows.Forms   # 仅用其 NotifyIcon 做托盘（WPF 无原生托盘）

# 通用行模型（列表行，供 WPF 双向绑定）：C1=启用勾选、C2=次勾选(语音)、T1..T4=文本列、CanEdit=可否改、
# Ref=原始对象、OnC1/OnC2=勾选切换后的回调（由 Add-CheckClickSelect 在勾选框 Click 时触发并回写/持久化）。
# 用【PowerShell 类】而非 Add-Type 编译 C#：纯源码、无需 csc.exe / 临时 DLL，受限令牌（如经 Lucy 等启动器
# 打开）下也能定义——运行时编译 C# 会报「客户端没有所需的特权」而使应用起不来。
# 代价：PS 类不支持带副作用的 setter，也无 INotifyPropertyChanged，故「切换即回写」改由 Click 路由事件驱动，
# 回调可能回滚 C1/C2（如系统项切换失败），由 Add-CheckClickSelect 在回调后把勾选框同步回模型值来体现。
class ShRow {
    [bool]   $C1
    [bool]   $C2
    [string] $T1
    [string] $T2
    [string] $T3
    [string] $T4
    [bool]   $CanEdit = $true
    [object] $Ref
    [object] $OnC1
    [object] $OnC2
}

# —— 暗色调色板（与预览一致）——
$script:W = @{
    Surface='#262B31'; Panel='#2E343B'; Header='#20252A'; Ink='#E4E7EB'; Muted='#8A94A0'
    Signal='#F0651A'; SignalHi='#FF7A2A'; Line='#3C434B'; Zebra='#2B3138'; Sel='#40301E'; Hover='#3A4149'
}

$script:IconPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets\logo.ico'))
function Get-AppIcon { if (-not $script:AppIcon) { try { $script:AppIcon = New-Object System.Drawing.Icon($script:IconPath) } catch { $script:AppIcon=$null } }; $script:AppIcon }
function Save-Config { Write-Config $script:Config $script:CfgPath }

# 暗色右键/下拉菜单（原生 ContextMenu 默认浅色）。返回空的暗色 ContextMenu，MenuItem 由内置隐式样式统一深色。
$script:DarkMenuXaml = @'
<ContextMenu xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
  Background="#2A3038" Foreground="#E6E9ED" FontFamily="Microsoft YaHei UI" FontSize="12">
  <ContextMenu.Template>
    <ControlTemplate TargetType="ContextMenu">
      <Border Background="#2A3038" BorderBrush="#3C434B" BorderThickness="1" CornerRadius="4" Padding="4" SnapsToDevicePixels="True">
        <StackPanel IsItemsHost="True"/>
      </Border>
    </ControlTemplate>
  </ContextMenu.Template>
  <ContextMenu.Resources>
    <Style TargetType="MenuItem">
      <Setter Property="Foreground" Value="#E6E9ED"/>
      <Setter Property="Background" Value="Transparent"/>
      <Setter Property="Template"><Setter.Value>
        <ControlTemplate TargetType="MenuItem">
          <Border x:Name="b" Background="Transparent" Padding="16,5" CornerRadius="3"><ContentPresenter ContentSource="Header" VerticalAlignment="Center"/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="#3A4149"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#8B95A1"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value></Setter>
    </Style>
  </ContextMenu.Resources>
</ContextMenu>
'@
function New-DarkContextMenu { [Windows.Markup.XamlReader]::Parse($script:DarkMenuXaml) }

$script:MainXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:sh="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="开机助手" Width="860" Height="720" MinWidth="680" MinHeight="520"
        WindowStartupLocation="CenterScreen" Background="#262B31" WindowStyle="None" ShowInTaskbar="True"
        FontFamily="Microsoft YaHei UI" TextOptions.TextFormattingMode="Display">
  <sh:WindowChrome.WindowChrome><sh:WindowChrome CaptionHeight="38" ResizeBorderThickness="6" CornerRadius="0" GlassFrameThickness="0"/></sh:WindowChrome.WindowChrome>
  <Window.Resources>
    <SolidColorBrush x:Key="Ink" Color="#E4E7EB"/><SolidColorBrush x:Key="Muted" Color="#8A94A0"/>
    <SolidColorBrush x:Key="Signal" Color="#F0651A"/><SolidColorBrush x:Key="Panel" Color="#2E343B"/><SolidColorBrush x:Key="Line" Color="#3C434B"/>
    <Style TargetType="TabControl"><Setter Property="Background" Value="#262B31"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="0"/></Style>
    <Style TargetType="TabItem">
      <Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="FontSize" Value="14"/>
      <Setter Property="Template"><Setter.Value>
        <ControlTemplate TargetType="TabItem"><Border x:Name="bd" Background="#262B31" BorderBrush="Transparent" BorderThickness="0,0,0,3" Padding="22,10" Cursor="Hand">
          <ContentPresenter ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
          <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="{StaticResource Ink}"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="bd" Property="BorderBrush" Value="{StaticResource Signal}"/><Setter TargetName="bd" Property="Background" Value="#2E343B"/><Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="FontWeight" Value="Bold"/></Trigger>
          </ControlTemplate.Triggers></ControlTemplate>
      </Setter.Value></Setter>
    </Style>
    <Style TargetType="DataGrid">
      <Setter Property="Background" Value="#262B31"/><Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="BorderThickness" Value="0"/>
      <Setter Property="RowBackground" Value="#262B31"/><Setter Property="AlternatingRowBackground" Value="#2B3138"/><Setter Property="GridLinesVisibility" Value="None"/>
      <Setter Property="HeadersVisibility" Value="Column"/><Setter Property="RowHeight" Value="30"/><Setter Property="FontSize" Value="13"/>
      <Setter Property="CanUserResizeRows" Value="False"/><Setter Property="SelectionMode" Value="Single"/><Setter Property="AutoGenerateColumns" Value="False"/><Setter Property="CanUserAddRows" Value="False"/>
    </Style>
    <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#262B31"/><Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="FontWeight" Value="Bold"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="8,6"/><Setter Property="Height" Value="30"/></Style>
    <Style TargetType="DataGridCell"><Setter Property="BorderThickness" Value="0"/><Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="VerticalContentAlignment" Value="Center"/>
      <Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#40301E"/><Setter Property="Foreground" Value="{StaticResource Ink}"/></Trigger></Style.Triggers></Style>
    <!-- 文本列内容垂直居中：DataGridCell 的 VerticalContentAlignment 对 DataGridTextColumn 生成的 TextBlock 不生效，须经 ElementStyle 直接设 TextBlock，才能与「启用」勾选框（模板列，居中）平齐。 -->
    <Style x:Key="CellText" TargetType="TextBlock"><Setter Property="VerticalAlignment" Value="Center"/></Style>
    <Style x:Key="Side" TargetType="Button">
      <Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="FontSize" Value="13"/><Setter Property="Height" Value="34"/><Setter Property="Margin" Value="0,0,0,10"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2E343B" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="3" Cursor="Hand"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Signal}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key="Chrome" TargetType="Button"><Setter Property="Width" Value="46"/><Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="FontFamily" Value="Segoe MDL2 Assets"/><Setter Property="FontSize" Value="10"/><Setter Property="sh:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="Transparent"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#3A4149"/><Setter Property="Foreground" Value="{StaticResource Ink}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
  </Window.Resources>

  <Grid>
    <Grid.RowDefinitions><RowDefinition Height="38"/><RowDefinition Height="*"/><RowDefinition Height="64"/></Grid.RowDefinitions>
    <Grid Grid.Row="0" Background="#20252A">
      <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="12,0">
        <Grid Width="16" Height="16"><Ellipse Stroke="#F0651A" StrokeThickness="1.6" Margin="1,2,1,0"/><Rectangle Fill="#F0651A" Width="1.6" Height="7" VerticalAlignment="Top" HorizontalAlignment="Center"/></Grid>
        <TextBlock Text="开机助手" Foreground="{StaticResource Ink}" FontSize="13" Margin="10,0,0,0" VerticalAlignment="Center"/>
      </StackPanel>
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
        <Button x:Name="BtnMin" Style="{StaticResource Chrome}" Content="&#xE921;"/><Button x:Name="BtnMax" Style="{StaticResource Chrome}" Content="&#xE922;"/><Button x:Name="BtnClose" Style="{StaticResource Chrome}" Content="&#xE8BB;"/>
      </StackPanel>
    </Grid>

    <TabControl Grid.Row="1" x:Name="Tabs" Margin="10,4,10,10">
      <TabItem Header="我的启动清单">
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="150"/></Grid.ColumnDefinitions>
          <DataGrid x:Name="GridLaunch" Grid.Column="0" Margin="0,8,0,0">
            <DataGrid.Columns>
              <DataGridTemplateColumn Header="启用" Width="54"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
              <DataGridTextColumn Header="类型" Binding="{Binding T1}" Width="96" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="摘要" Binding="{Binding T2}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="延时(ms)" Binding="{Binding T3}" Width="80" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
            </DataGrid.Columns>
          </DataGrid>
          <StackPanel Grid.Column="1" Margin="10,8,0,0">
            <Button x:Name="LAdd" Content="新增 ▾" Style="{StaticResource Side}"/><Button x:Name="LEdit" Content="编辑" Style="{StaticResource Side}"/>
            <Button x:Name="LDel" Content="删除" Style="{StaticResource Side}"/><Button x:Name="LUp" Content="上移" Style="{StaticResource Side}" Margin="0,14,0,10"/><Button x:Name="LDown" Content="下移" Style="{StaticResource Side}"/><Button x:Name="LTest" Content="运行" Style="{StaticResource Side}" Margin="0,14,0,0"/>
          </StackPanel>
        </Grid>
      </TabItem>
      <TabItem Header="定时提醒">
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="150"/></Grid.ColumnDefinitions>
          <DataGrid x:Name="GridRemind" Grid.Column="0" Margin="0,8,0,0">
            <DataGrid.Columns>
              <DataGridTemplateColumn Header="启用" Width="54"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
              <DataGridTextColumn Header="时间" Binding="{Binding T1}" Width="88" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="周期" Binding="{Binding T2}" Width="120" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="文本" Binding="{Binding T3}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTemplateColumn Header="语音" Width="54"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C2, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C2" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
            </DataGrid.Columns>
          </DataGrid>
          <StackPanel Grid.Column="1" Margin="10,8,0,0">
            <Button x:Name="RAdd" Content="新增" Style="{StaticResource Side}"/><Button x:Name="REdit" Content="编辑" Style="{StaticResource Side}"/>
            <Button x:Name="RDel" Content="删除" Style="{StaticResource Side}"/><Button x:Name="RTest" Content="运行" Style="{StaticResource Side}" Margin="0,14,0,10"/>
          </StackPanel>
        </Grid>
      </TabItem>
      <TabItem Header="系统启动项">
        <Grid><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,8,0,0">
            <TextBlock Text="过滤" Foreground="{StaticResource Ink}" VerticalAlignment="Center" FontSize="13" Margin="0,0,8,0"/>
            <TextBox x:Name="SSearch" Width="320" Height="30" Background="#2E343B" Foreground="#E6E9ED" BorderBrush="#3C434B" BorderThickness="1" Padding="6,4" FontSize="13" CaretBrush="#E6E9ED"/>
            <TextBlock Text="按名称或命令过滤" Foreground="{StaticResource Muted}" VerticalAlignment="Center" FontSize="12" Margin="10,0,0,0"/>
          </StackPanel>
          <DataGrid x:Name="GridSystem" Grid.Row="1" Margin="0,8,0,0">
            <DataGrid.Columns>
              <DataGridTemplateColumn Header="启用" Width="54"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
              <DataGridTextColumn Header="名称" Binding="{Binding T1}" Width="200" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="命令" Binding="{Binding T2}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="来源" Binding="{Binding T3}" Width="120" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="范围" Binding="{Binding T4}" Width="130" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
            </DataGrid.Columns>
          </DataGrid>
          <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,8,0,0">
            <Button x:Name="SRefresh" Content="刷新" Style="{StaticResource Side}" Width="100" Height="28" Margin="0,0,10,0"/>
            <Button x:Name="SImport" Content="纳入启动清单" Style="{StaticResource Side}" Width="130" Height="28" Margin="0"/>
          </StackPanel>
          <TextBlock x:Name="SHint" Grid.Row="3" Text="勾选/取消即时生效（非删除，可恢复）；标「需管理员」的项需管理员身份；置灰的「只读」项为系统/策略项，无法开关。" Foreground="{StaticResource Muted}" TextWrapping="Wrap" Margin="0,8,0,0" FontSize="12"/>
        </Grid>
      </TabItem>
      <TabItem Header="动作组">
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="150"/></Grid.ColumnDefinitions>
          <DataGrid x:Name="GridGroup" Grid.Column="0" Margin="0,8,0,0">
            <DataGrid.Columns>
              <DataGridTemplateColumn Header="启用" Width="54"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
              <DataGridTextColumn Header="名称" Binding="{Binding T1}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="步骤" Binding="{Binding T2}" Width="80" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
            </DataGrid.Columns>
          </DataGrid>
          <StackPanel Grid.Column="1" Margin="10,8,0,0">
            <Button x:Name="GAdd" Content="新增 ▾" Style="{StaticResource Side}"/><Button x:Name="GEdit" Content="编辑" Style="{StaticResource Side}"/>
            <Button x:Name="GDel" Content="删除" Style="{StaticResource Side}"/><Button x:Name="GRun" Content="运行" Style="{StaticResource Side}" Margin="0,14,0,10"/>
          </StackPanel>
        </Grid>
      </TabItem>
    </TabControl>

    <Border Grid.Row="2" BorderBrush="{StaticResource Line}" BorderThickness="0,1,0,0">
      <Grid>
        <Button x:Name="BtnRun" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="16,0" Height="40" Width="210" Foreground="White" FontWeight="Bold" FontSize="14" Cursor="Hand">
          <Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#F0651A" CornerRadius="2"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
            <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#FF7A2A"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template>
          <TextBlock Text="▶  重新运行启动清单"/>
        </Button>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,16,0">
          <TextBlock Text="开机延迟" Foreground="{StaticResource Muted}" FontSize="12" VerticalAlignment="Center"
                     ToolTip="仅开机自启时生效：登录后固定等这么多秒、让登录风暴过峰，再跑启动清单。手动「重新运行」不受影响。开得太早/程序没打开就把它调大。"/>
          <TextBox x:Name="TxtDelay" Width="40" Margin="6,0,3,0" Padding="2,1" TextAlignment="Center" FontSize="12" VerticalContentAlignment="Center"
                   MaxLength="3" ToolTip="0–600 秒。开机自启后固定等待的秒数（唯一的延时杠杆，不够就往大调）。"/>
          <TextBlock Text="秒" Foreground="{StaticResource Muted}" FontSize="12" VerticalAlignment="Center" Margin="0,0,16,0"/>
          <CheckBox x:Name="ChkMin" Content="启动时最小化到托盘" VerticalAlignment="Center" Foreground="{StaticResource Muted}" FontSize="12" Cursor="Hand"/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Window>
'@

# 把启动步骤投影成显示行（ShRow）。启用勾选回写 step.enabled 并保存。
function Get-LaunchRows {
    param($Config)
    $rows = New-Object System.Collections.ObjectModel.ObservableCollection[object]
    foreach ($st in @($Config.launchSteps)) {
        $r = New-Object ShRow
        $r.C1 = [bool]$st.enabled
        $r.T1 = Get-StepKindLabel $st.kind
        $r.T2 = Format-StepListSummary $st
        $r.T3 = [string][int]$st.delayMs
        $r.Ref = $st
        $r.OnC1 = { param($row) $row.Ref.enabled = $row.C1; Save-Config }
        $rows.Add($r)
    }
    ,$rows   # 逗号防止集合被展开（单元素时 ItemsSource 会收到单个 ShRow）
}

# Get-StepKindLabel / Get-StepSummary / Get-DaysLabel 复用 Core.ps1 的规范实现（不在此重复定义，避免遮蔽）。
function Get-ReminderRows {
    param($Config)
    $rows = New-Object System.Collections.ObjectModel.ObservableCollection[object]
    foreach ($r in @($Config.reminders)) {
        $row = New-Object ShRow
        $row.C1 = [bool]$r.enabled
        if ([string]$r.trigger -eq 'startup') {
            $sfx = switch ([string]$r.startupHourMode) { 'before'{"·$([int]$r.startupHour)点前"} 'after'{"·$([int]$r.startupHour)点后"} default{''} }
            $row.T1 = "登录时$sfx"
        } else { $row.T1 = [string]$r.time }
        $row.T2 = switch ([string]$r.recurType) { 'everyNDays'{"每$([int]$r.intervalDays)天"} 'monthly'{"每月$([int]$r.monthlyDay)号"} default{ Get-DaysLabel $r.days } }
        $sum = ([string]$r.message -replace "`r?`n",' '); if ($sum.Length -gt 30){ $sum=$sum.Substring(0,30)+'…' }
        $row.T3 = $sum; $row.C2 = [bool]$r.speak; $row.Ref = $r
        $row.OnC1 = { param($x) $x.Ref.enabled = $x.C1; Save-Config }
        $row.OnC2 = { param($x) $x.Ref.speak  = $x.C2; Save-Config }
        $rows.Add($row)
    }
    ,$rows   # 逗号防止 PowerShell 把集合展开成元素（否则单元素时 ItemsSource 收到的是单个 ShRow 而非集合）
}
function Get-GroupRows {
    param($Config)
    $rows = New-Object System.Collections.ObjectModel.ObservableCollection[object]
    foreach ($g in @($Config.actionGroups)) {
        $row = New-Object ShRow
        $row.C1 = [bool]$g.enabled; $row.T1 = [string]$g.name; $row.T2 = [string]@($g.steps).Count; $row.Ref = $g
        $row.OnC1 = { param($x) $x.Ref.enabled = $x.C1; Save-Config }
        $rows.Add($row)
    }
    ,$rows   # 逗号防止 PowerShell 把集合展开成元素（否则单元素时 ItemsSource 收到的是单个 ShRow 而非集合）
}
function Get-SystemRows {
    param($Items)
    $rows = New-Object System.Collections.ObjectModel.ObservableCollection[object]
    foreach ($it in @($Items)) {
        $canT = if ($it.PSObject.Properties['canToggle']) { [bool]$it.canToggle } else { $true }
        $src = Get-TypeLabel $it.type
        if (-not $canT) { $note = if ($it.readOnlyNote) { "只读·$($it.readOnlyNote)" } else { '只读' }; $src = "$src · $note" }
        $row = New-Object ShRow
        $row.C1 = [bool]$it.enabled; $row.CanEdit = $canT; $row.T1 = [string]$it.name; $row.T2 = [string]$it.command; $row.T3 = $src
        $row.T4 = Get-ScopeLabel $it.scope ([bool]$it.needsAdmin); $row.Ref = $it
        $rows.Add($row)
    }
    ,$rows   # 逗号防止 PowerShell 把集合展开成元素（否则单元素时 ItemsSource 收到的是单个 ShRow 而非集合）
}

# 勾选框由 DataGridCheckBoxColumn 改为模板 CheckBox 后，点勾选框不再顺带选中该行（模板 CheckBox 会
# 吞掉 MouseDown，DataGridCell 收不到、不选行）。而「编辑/删除/上移下移/纳入」等按钮都按 SelectedItem
# 取行——不同步就会对错行操作。补一个类处理器：点某行勾选框即把该行设为选中。四个 DataGrid 共用。
# 同时承担【勾选切换后的持久化】：ShRow 是 PS 类、无 setter 副作用/INotifyPropertyChanged（改用 PS 类是
# 为了免运行时编译、受限令牌下也能起），故在此 Click 事件里按勾选框 Tag(C1/C2) 调 OnC1/OnC2 回调；回调可能
# 回滚 C1/C2（如系统项切换失败），回调后把勾选框 IsChecked 同步回模型值来反映（替代原 INPC 的 源->目标 更新）。
function Add-CheckClickSelect {
    param($Grid)
    $Grid.AddHandler([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent,
        [System.Windows.RoutedEventHandler]{ param($s,$e)
            # 冒泡到 DataGrid 时 $e.Source 会被重定成 DataGrid，故从 $e.OriginalSource（被点的最深视觉元素，
            # 位于勾选框模板内部）往上找到那个勾选框本身，才能拿到它的 Tag(C1/C2) 和 DataContext(ShRow)。
            $cb = $e.OriginalSource
            while ($cb -and $cb -isnot [System.Windows.Controls.Primitives.ToggleButton]) {
                $cb = if ($cb -is [System.Windows.Media.Visual] -or $cb -is [System.Windows.Media.Media3D.Visual3D]) { [System.Windows.Media.VisualTreeHelper]::GetParent($cb) } else { $null }
            }
            if (-not $cb) { return }   # 只有勾选框触发的 Click 才走持久化（其它 ButtonBase 不管）
            $row = $cb.DataContext
            if ($row -isnot [ShRow]) { return }
            $s.SelectedItem = $row
            if ([string]$cb.Tag -eq 'C2') {
                if ($row.OnC2) { try { & $row.OnC2 $row } catch {} ; $cb.IsChecked = [bool]$row.C2 }
            } else {
                if ($row.OnC1) { try { & $row.OnC1 $row } catch {} ; $cb.IsChecked = [bool]$row.C1 }
            }
        }, $true)
}

# 三个列表 Tab（启动/提醒/动作组）的公共 CRUD 粘合：重载 / 选中索引 / 双击=编辑 / 删除。这四件事三处完全相同，
# 仅「配置集合属性名」「行生成器」「编辑/删除按钮名」不同。返回 @{Grid;Sel;Reload} 供各 Tab 差异化的
# 新增/编辑/上移下移/运行/测试 处理器复用。$RowsFn 形如 { param($c) Get-LaunchRows $c }。
function Register-CrudGrid {
    param($Win, $Config, [string]$GridName, [string]$Prop, [scriptblock]$RowsFn, [string]$EditBtn, [string]$DelBtn, [scriptblock]$BeforeDelete)
    $g = $Win.FindName($GridName)
    Add-CheckClickSelect $g   # 点勾选框即选中该行（供 编辑/删除/上下移 的 SelectedItem 取到正确行）
    $reload = { $g.ItemsSource = (& $RowsFn $Config) }.GetNewClosure()
    & $reload
    $sel = { if ($null -eq $g.SelectedItem) { -1 } else { [array]::IndexOf(@($Config.$Prop), $g.SelectedItem.Ref) } }.GetNewClosure()
    $g.Add_MouseDoubleClick({ if((& $sel) -ge 0){ $Win.FindName($EditBtn).RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent))) } }.GetNewClosure())
    # $BeforeDelete（可选）：删除前守卫，返回 $false 取消（动作组用它做「被引用则确认并清引用」）。
    # 删除后自动选中原位置的下一条（删的是末尾则选新末尾），连续删除不用反复点选。
    $Win.FindName($DelBtn).Add_Click({ $i=& $sel; if($i -ge 0){ $col=@($Config.$Prop); if($BeforeDelete -and -not (& $BeforeDelete $col[$i])){ return }; $Config.$Prop=@($col | Where-Object { $_ -ne $col[$i] }); Save-Config; & $reload; $n=@($Config.$Prop).Count; if($n -gt 0){ $g.SelectedIndex=[Math]::Min($i,$n-1) } } }.GetNewClosure())
    @{ Grid=$g; Sel=$sel; Reload=$reload }
}

function Show-WpfMainWindow {
    param($Config)
    $win = [Windows.Markup.XamlReader]::Parse($script:MainXaml)
    $ic = Get-AppIcon; if ($ic) { try { $win.Icon = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon($ic.Handle, [System.Windows.Int32Rect]::Empty, [System.Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions()) } catch {} }

    # 标题栏按钮
    $win.FindName('BtnMin').Add_Click({ $win.WindowState='Minimized' }.GetNewClosure())
    $win.FindName('BtnMax').Add_Click({ if($win.WindowState -eq 'Maximized'){$win.WindowState='Normal'}else{$win.WindowState='Maximized'} }.GetNewClosure())
    $win.FindName('BtnClose').Add_Click({ $win.Hide() }.GetNewClosure())   # 关闭=隐藏到托盘
    # 退出标志放 Tag：闭包里 $script:WpfReallyExit 解析到闭包模块的空作用域、恒为 $null（同下方
    # $script:Tray 的坑）——原写法「真退出」也总 Cancel，只是靠 WPF 在 Application.Shutdown 期间
    # 忽略 Closing 取消才没卡住退出。
    $win.Add_Closing({ param($s,$e) if (-not $s.Tag.ReallyExit) { $e.Cancel = $true; $s.Hide() } }.GetNewClosure())

    # 启动清单 Tab（选中/重载/双击/删除 统一由 Register-CrudGrid 接线）
    $crudL = Register-CrudGrid $win $Config 'GridLaunch' 'launchSteps' { param($c) Get-LaunchRows $c } 'LEdit' 'LDel'
    $gl = $crudL.Grid; $selIdx = $crudL.Sel; $reloadLaunch = $crudL.Reload

    # 新增：类型菜单（启动清单可含 app/keys/text/volume/window/system/delay/group，不含 message）
    $lAddMenu = New-DarkContextMenu
    foreach ($k in @(@('启动程序','app'),@('发送按键','keys'),@('发送文本','text'),@('音量','volume'),@('窗口动作','window'),@('系统命令','system'),@('延时','delay'),@('动作组','group'))) {
        $mi=New-Object System.Windows.Controls.MenuItem; $mi.Header=$k[0]; $mi.Tag=$k[1]
        $mi.Add_Click({ param($s,$e) $n=Show-StepDialogWpf ([string]$s.Tag) $null $Config.actionGroups $win; if($n){ $r=Add-ItemAfter $Config.launchSteps $n (& $selIdx); $Config.launchSteps=@($r.Items); Save-Config; & $reloadLaunch; $gl.SelectedIndex=$r.NewIndex; try{$gl.ScrollIntoView($gl.SelectedItem)}catch{} } }.GetNewClosure())
        [void]$lAddMenu.Items.Add($mi)
    }
    $win.FindName('LAdd').Add_Click({ $lAddMenu.PlacementTarget=$win.FindName('LAdd'); $lAddMenu.IsOpen=$true }.GetNewClosure())
    $win.FindName('LEdit').Add_Click({ $i=& $selIdx; if ($i -ge 0) { $n=Show-StepDialogWpf ([string]$Config.launchSteps[$i].kind) $Config.launchSteps[$i] $Config.actionGroups $win; if ($n) { $n.enabled=$Config.launchSteps[$i].enabled; $Config.launchSteps[$i]=$n; Save-Config; & $reloadLaunch; $gl.SelectedIndex=$i } } }.GetNewClosure())
    # 双击=编辑、删除 由 Register-CrudGrid 统一处理
    $win.FindName('LUp').Add_Click({ $i=& $selIdx; if ($i -gt 0) { $l=@($Config.launchSteps); $t=$l[$i]; $l[$i]=$l[$i-1]; $l[$i-1]=$t; $Config.launchSteps=$l; Save-Config; & $reloadLaunch; $gl.SelectedIndex=$i-1 } }.GetNewClosure())
    $win.FindName('LDown').Add_Click({ $i=& $selIdx; $l=@($Config.launchSteps); if ($i -ge 0 -and $i -lt $l.Count-1) { $t=$l[$i]; $l[$i]=$l[$i+1]; $l[$i+1]=$t; $Config.launchSteps=$l; Save-Config; & $reloadLaunch; $gl.SelectedIndex=$i+1 } }.GetNewClosure())
    # 运行：单步无状态，跑=测。无视 enabled/时间条件，就跑选中这一步（与提醒「运行」一致，纯预览）。group 走动作组执行。
    $win.FindName('LTest').Add_Click({ $i=& $selIdx; if ($i -ge 0) { $st=$Config.launchSteps[$i]
        if ([string]$st.kind -eq 'group') {
            $g=Resolve-ActionGroup $Config.actionGroups ([string]$st.groupId)
            if (-not $g) { [System.Windows.MessageBox]::Show('引用的动作组不存在（可能已删除）。','运行')|Out-Null }
            elseif (-not $g.enabled) { [System.Windows.MessageBox]::Show("动作组「$($g.name)」已禁用，请先启用。",'运行')|Out-Null }   # 与动作组页 GRun 一致：禁用组不代跑（可能含关窗/关机等破坏性步骤）
            else { Invoke-ActionGroupAsync $g }
        } else { Invoke-StepActionAsync $st $win.Tag.Tray }
    } }.GetNewClosure())

    # 托盘经 $win.Tag.Tray 取（Add-WpfTray 稍后填入）：GetNewClosure 闭包里 $script:Tray 解析到闭包模块的
    # 空作用域 → 传 $null → 完成气泡（含「N 步有警告」提示）从主窗按钮触发时永远不弹。
    $win.FindName('BtnRun').Add_Click({ Invoke-LaunchSequenceAsync $Config $win.Tag.Tray }.GetNewClosure())
    $chkMin = $win.FindName('ChkMin'); $chkMin.IsChecked = [bool]$Config.settings.startMinimized
    $chkMin.Add_Click({ $Config.settings.startMinimized = [bool]$chkMin.IsChecked; Save-Config }.GetNewClosure())

    # 开机延迟（秒）：仅作用于开机自启（-Boot）。失焦时校验并回写（0–600，非数字归 0），存前规范化显示。
    $txtDelay = $win.FindName('TxtDelay')
    $txtDelay.Text = [string]([int]$Config.settings.startupDelaySeconds)
    $commitDelay = {
        $v = 0; [void][int]::TryParse(($txtDelay.Text -replace '[^\d]', ''), [ref]$v)
        if ($v -lt 0) { $v = 0 } elseif ($v -gt 600) { $v = 600 }
        $txtDelay.Text = [string]$v
        if ([int]$Config.settings.startupDelaySeconds -ne $v) { $Config.settings.startupDelaySeconds = $v; Save-Config }
    }.GetNewClosure()
    $txtDelay.Add_LostFocus($commitDelay)

    # —— 定时提醒 Tab ——（选中/重载/双击/删除 统一由 Register-CrudGrid 接线）
    $crudR = Register-CrudGrid $win $Config 'GridRemind' 'reminders' { param($c) Get-ReminderRows $c } 'REdit' 'RDel'
    $gr = $crudR.Grid; $selR = $crudR.Sel; $reloadRemind = $crudR.Reload
    $win.FindName('RAdd').Add_Click({ $n=Show-ReminderDialogWpf $null $Config.actionGroups $win; if($n){ $r=Add-ItemAfter $Config.reminders $n (& $selR); $Config.reminders=@($r.Items); Save-Config; & $reloadRemind; $gr.SelectedIndex=$r.NewIndex; try{$gr.ScrollIntoView($gr.SelectedItem)}catch{} } }.GetNewClosure())
    $win.FindName('REdit').Add_Click({ $i=& $selR; if($i -ge 0){ $n=Show-ReminderDialogWpf $Config.reminders[$i] $Config.actionGroups $win; if($n){ $n.enabled=$Config.reminders[$i].enabled; $n.id=$Config.reminders[$i].id; $Config.reminders[$i]=$n; Save-Config; & $reloadRemind; $gr.SelectedIndex=$i } } }.GetNewClosure())
    $win.FindName('RTest').Add_Click({ $i=& $selR; if($i -ge 0){ Invoke-ReminderAsync $Config.reminders[$i] $Config.actionGroups ({ param($out) }) } }.GetNewClosure())   # 后台弹，不锁主窗；纯预览不推进状态

    # —— 动作组 Tab ——（选中/重载/双击/删除 统一由 Register-CrudGrid 接线）
    # 删除守卫：组若被提醒（点是后/静默）或启动步骤引用，删除会让那些引用变成「静默失效」——先确认，同意则连引用一并清理。
    $gDelGuard = { param($g)
        $gid=[string]$g.id
        $refR=@($Config.reminders | Where-Object { [string]$_.silentGroupId -eq $gid -or ($_.onYes -and [string]$_.onYes.type -eq 'group' -and [string]$_.onYes.target -eq $gid) })
        $refS=@($Config.launchSteps | Where-Object { [string]$_.kind -eq 'group' -and [string]$_.groupId -eq $gid })
        if($refR.Count -eq 0 -and $refS.Count -eq 0){ return $true }
        $r=[System.Windows.MessageBox]::Show("动作组「$($g.name)」正被 $($refR.Count) 条提醒、$($refS.Count) 个启动步骤引用。`n删除后这些引用将一并清除（提醒改为仅弹窗、相关启动步骤移除）。仍要删除吗？",'删除动作组',[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Warning)
        if($r -ne [System.Windows.MessageBoxResult]::Yes){ return $false }
        foreach($rm in $refR){
            if([string]$rm.silentGroupId -eq $gid){ $rm.silentGroupId='' }
            if($rm.onYes -and [string]$rm.onYes.type -eq 'group' -and [string]$rm.onYes.target -eq $gid){ $rm.onYes=@{ type='none'; target='' } }
        }
        if($refS.Count -gt 0){ $Config.launchSteps=@($Config.launchSteps | Where-Object { -not ([string]$_.kind -eq 'group' -and [string]$_.groupId -eq $gid) }) }
        & $reloadRemind; & $reloadLaunch
        $true
    }.GetNewClosure()
    $crudG = Register-CrudGrid $win $Config 'GridGroup' 'actionGroups' { param($c) Get-GroupRows $c } 'GEdit' 'GDel' -BeforeDelete $gDelGuard
    $gg = $crudG.Grid; $selG = $crudG.Sel; $reloadGroup = $crudG.Reload
    # 新增：空白 + 从模板（模板点开进编辑器预填，可改进程名/应用后再保存）
    $gAddMenu = New-DarkContextMenu
    $addGroup = { param($n) if($n){ $r=Add-ItemAfter $Config.actionGroups $n (& $selG); $Config.actionGroups=@($r.Items); Save-Config; & $reloadGroup; $gg.SelectedIndex=$r.NewIndex; try{$gg.ScrollIntoView($gg.SelectedItem)}catch{} } }.GetNewClosure()
    $miBlank = New-Object System.Windows.Controls.MenuItem; $miBlank.Header='空白动作组'
    $miBlank.Add_Click({ & $addGroup (Show-ActionGroupDialogWpf $null $win) }.GetNewClosure()); [void]$gAddMenu.Items.Add($miBlank)
    $hdrT = New-Object System.Windows.Controls.MenuItem; $hdrT.Header='—— 从模板 ——'; $hdrT.IsEnabled=$false; [void]$gAddMenu.Items.Add($hdrT)
    $tplNames = @(Get-ActionGroupTemplates | ForEach-Object { $_.name })
    for($ti=0; $ti -lt $tplNames.Count; $ti++){
        $mi=New-Object System.Windows.Controls.MenuItem; $mi.Header=$tplNames[$ti]; $mi.Tag=$ti
        $mi.Add_Click({ param($s,$e) & $addGroup (Show-ActionGroupDialogWpf (Get-ActionGroupTemplates)[[int]$s.Tag] $win) }.GetNewClosure())
        [void]$gAddMenu.Items.Add($mi)
    }
    $win.FindName('GAdd').Add_Click({ $gAddMenu.PlacementTarget=$win.FindName('GAdd'); $gAddMenu.IsOpen=$true }.GetNewClosure())
    $win.FindName('GEdit').Add_Click({ $i=& $selG; if($i -ge 0){ $n=Show-ActionGroupDialogWpf $Config.actionGroups[$i] $win; if($n){ $n.enabled=$Config.actionGroups[$i].enabled; $Config.actionGroups[$i]=$n; Save-Config; & $reloadGroup; $gg.SelectedIndex=$i } } }.GetNewClosure())
    # 双击=编辑、删除 由 Register-CrudGrid 统一处理
    $win.FindName('GRun').Add_Click({ $i=& $selG; if($i -ge 0){ $g=$Config.actionGroups[$i]; if(-not $g.enabled){ [System.Windows.MessageBox]::Show("动作组「$($g.name)」已禁用，请先启用。",'开机助手')|Out-Null; return }; Invoke-ActionGroupAsync $g } }.GetNewClosure())

    # —— 系统启动项 Tab（首次进入懒加载，后台枚举）——
    $gs = $win.FindName('GridSystem')
    $ss = $win.FindName('SSearch')
    Add-CheckClickSelect $gs   # 点勾选框即选中该行（「纳入启动清单」按 SelectedItem 取行，否则会对错行操作）
    $sysState = @{ Loaded=$false; Loading=$false; AllRows=$null }
    # 搜索/过滤：按名称(T1)或命令(T2)不区分大小写过滤；过滤后 ItemsSource 仍用同一批 ShRow（保住 OnC1/勾选态）。
    # 用 IndexOf 不用通配/正则，含 [ 等字符也不炸。
    $applySysFilter = {
        if ($null -eq $sysState.AllRows) { return }
        $q = $ss.Text.Trim()
        $view = New-Object System.Collections.ObjectModel.ObservableCollection[object]
        foreach ($r in $sysState.AllRows) {
            if (-not $q -or ([string]$r.T1).IndexOf($q,[System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or ([string]$r.T2).IndexOf($q,[System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $view.Add($r) }
        }
        $gs.ItemsSource = $view
    }.GetNewClosure()
    $ss.Add_TextChanged($applySysFilter)
    # OnDone 必须是「单层」闭包（直接从本函数作用域 GetNewClosure 捕获 $gs/$sysState）。
    # 若把它嵌在 $loadSys 里再 GetNewClosure，内层闭包捕获不到外层闭包的 $gs → 回调里 $gs 非法、$gs.ItemsSource 抛「找不到属性」。
    $onSysLoaded = {
        param($items)
        $rows = Get-SystemRows $items
        foreach ($rw in $rows) {
            $rw.OnC1 = { param($x)
                if ([bool]$x.C1 -eq [bool]$x.Ref.enabled) { return }
                $canT = if ($x.Ref.PSObject.Properties['canToggle']) { [bool]$x.Ref.canToggle } else { $true }
                if (-not $canT) { $x.C1 = [bool]$x.Ref.enabled; return }
                $res = Set-SystemStartupItemEnabled $x.Ref $x.C1
                if ($res -eq 'Ok') { $x.Ref.enabled = $x.C1 }
                elseif ($res -eq 'NeedsAdmin') { $x.C1 = [bool]$x.Ref.enabled; Show-NeedsAdminPrompt "修改「$($x.Ref.name)」" }
                else { $x.C1 = [bool]$x.Ref.enabled; [System.Windows.MessageBox]::Show("修改「$($x.Ref.name)」失败：`n$res",'系统启动项')|Out-Null }
            }
        }
        $sysState.AllRows = $rows; & $applySysFilter; $sysState.Loaded=$true; $sysState.Loading=$false
    }.GetNewClosure()
    $loadSys = {
        if ($sysState.Loading) { return }
        $sysState.Loading = $true; $gs.ItemsSource = $null
        Start-SystemItemsAsyncWpf $win $onSysLoaded
    }.GetNewClosure()
    $win.FindName('SRefresh').Add_Click({ $sysState.Loaded=$false; & $loadSys }.GetNewClosure())
    $win.FindName('SImport').Add_Click({
        $r=$gs.SelectedItem; if($null -eq $r){ return }
        $res = Import-StartupItemToChecklist $r.Ref $Config
        if($res -eq 'Ok'){ Save-Config; & $reloadLaunch; $sysState.Loaded=$false; & $loadSys; [System.Windows.MessageBox]::Show("已纳入清单并停用原项「$($r.Ref.name)」。",'纳入启动清单')|Out-Null }
        elseif($res -eq 'NeedsAdmin'){ Show-NeedsAdminPrompt "纳入「$($r.Ref.name)」" }
        else{ [System.Windows.MessageBox]::Show("纳入失败：`n$res",'纳入启动清单')|Out-Null }
    }.GetNewClosure())
    $win.FindName('Tabs').Add_SelectionChanged({ param($s,$e) if ($e.OriginalSource -is [System.Windows.Controls.TabControl] -and $s.SelectedIndex -eq 2 -and -not $sysState.Loaded) { & $loadSys } }.GetNewClosure())

    $win.Tag = [pscustomobject]@{ Config=$Config; ReloadLaunch=$reloadLaunch; ReloadGroup=$reloadGroup; Dnd=@{ Until=$null }; ReallyExit=$false }
    $script:MainWin = $win
    $win
}

# NeedsAdmin 统一处理：未提权 → 询问「以管理员身份重开」（README 承诺的流程）；已提权仍失败 → 如实报权限不足。
# 系统启动项勾选、纳入清单、开机自启注册三处共用。
function Show-NeedsAdminPrompt {
    param([string]$What)
    if (Test-IsElevated) { [System.Windows.MessageBox]::Show("操作失败：权限不足（$What）。",'开机助手')|Out-Null; return }
    $r = [System.Windows.MessageBox]::Show("「$What」需要管理员权限。`n是否以管理员身份重新打开本程序？",'需要管理员权限',[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Question)
    if ($r -eq 'Yes') { if (Restart-Elevated $script:SelfPath) { Stop-WpfApp } }
}

function Show-MainWin { param($Win) $Win.Show(); if ($Win.WindowState -eq 'Minimized') { $Win.WindowState='Normal' }; $Win.ShowInTaskbar=$true; [void]$Win.Activate() }
function Stop-WpfApp { try { $script:MainWin.Tag.ReallyExit=$true } catch {}; try { if ($script:Tray) { $script:Tray.Visible=$false } } catch {}; try { [System.Windows.Application]::Current.Shutdown() } catch { try { $script:MainWin.Dispatcher.InvokeShutdown() } catch {} } }
function Set-WpfDnd { param($Win, $Tray, [int]$Hours) $Win.Tag.Dnd.Until=(Get-Date).AddHours($Hours); Show-TrayNotify $Tray '开机助手' "已暂停提醒 $Hours 小时" }

function Add-WpfTray {
    param($Win, $Config)
    $self = $script:SelfPath
    $appRoot = $script:AppRoot
    $autostart = @{ Registered=$false; UserSet=$false; Busy=$false; Pending=$false }
    # 注册/注销开机自启完成后回 UI 线程处理结果（单层闭包，捕获 $autostart/$self；避免嵌套闭包捕获失败）。
    $onAutoDone = { param($out)
        $autostart.Busy=$false; $reg=[bool]$autostart.Pending; $res=[string]($out|Select-Object -Last 1)
        if($res -eq 'Ok'){ $autostart.Registered=$reg; $autostart.UserSet=$true; [System.Windows.MessageBox]::Show($(if($reg){'已注册为登录时自启（最高权限）。'}else{'已取消开机自启。'}),'开机助手')|Out-Null }
        elseif($res -eq 'NeedsAdmin'){ Show-NeedsAdminPrompt $(if($reg){'设为开机自启'}else{'取消开机自启'}) }
        else { [System.Windows.MessageBox]::Show("开机自启操作失败：`n$res",'开机助手')|Out-Null }
    }.GetNewClosure()
    if ($appRoot) {
        Invoke-InRunspaceAsync -Vars @{ appRoot=$appRoot } -OnDone ({ param($out) if(-not $autostart.UserSet){ $autostart.Registered=[bool]($out|Select-Object -Last 1) } }.GetNewClosure()) -Script { . (Join-Path $appRoot 'lib\StartupHelper.Core.ps1'); . (Join-Path $appRoot 'lib\StartupHelper.Actions.ps1'); [bool](Test-AutostartRegistered) }
    }
    $tray = New-Object System.Windows.Forms.NotifyIcon
    $tray.Text='开机助手'; $ic=Get-AppIcon; $tray.Icon = if($ic){$ic}else{[System.Drawing.SystemIcons]::Application}; $tray.Visible=$true

    # 托盘菜单改用 WPF ContextMenu（矢量、DPI 正确、暗色）。WinForms ContextMenuStrip 在本进程「手动声明 DPI 感知」
    # 下不随 150% 缩放放大、渲染成极小看不清，且无法可靠修好。扁平化（动作组/暂停内联）以套用现成暗色扁平模板。
    try { Add-Type -Namespace SHFg -Name Win -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);' -ErrorAction Stop } catch {}
    # 不设 PlacementTarget！挂上「从未显示过的窗口」（启动最小化/-Run 时主窗无 PresentationSource）会让
    # ContextMenu 打开后立刻静默自灭（IsOpen 立即回 False、无异常、无日志）——即「右键托盘菜单不出来」的真凶。
    # MousePoint 定位不需要目标视觉，独立 ContextMenu 自建弹出层即可。
    $cm = New-DarkContextMenu

    # 开机自启点击逻辑（后台跑，不冻 UI）。抽成闭包供菜单项复用。
    $onAutoClick = {
        if($autostart.Busy){ return }
        $autostart.Busy=$true; $autostart.Pending=(-not $autostart.Registered)
        Show-TrayNotify $tray '开机助手' '正在修改开机自启…'
        Invoke-InRunspaceAsync -Vars @{ appRoot=$appRoot; scriptPath=$self; reg=$autostart.Pending } -OnDone $onAutoDone -Script {
            . (Join-Path $appRoot 'lib\StartupHelper.Core.ps1'); . (Join-Path $appRoot 'lib\StartupHelper.Win32.ps1'); . (Join-Path $appRoot 'lib\StartupHelper.Actions.ps1')
            if($reg){ Register-Autostart $scriptPath } else { Unregister-Autostart }
        }
    }.GetNewClosure()

    $addMi = { param($text, $click, $enabled)
        $mi = New-Object System.Windows.Controls.MenuItem; $mi.Header = $text
        if ($null -ne $enabled) { $mi.IsEnabled = [bool]$enabled }
        if ($click) { $mi.Add_Click($click) }
        [void]$cm.Items.Add($mi); $mi
    }.GetNewClosure()
    $addSep = { [void]$cm.Items.Add((New-Object System.Windows.Controls.Separator)) }.GetNewClosure()

    # 每次打开前重建：动作组列表 / 暂停剩余时间 / 自启状态 都是动态的
    $buildMenu = {
        # 菜单项闭包是嵌套在本闭包里的闭包：GetNewClosure 只捕获本层局部变量，$Win/$tray/$Config
        # 在本闭包的模块作用域捕获不到（同 Start-WpfReminderTimer OnDone 的坑）——此前全靠主脚本
        # 顶层恰好有同名全局变量兜底才能用。先取局部引用，让菜单项闭包真正捕获到。
        $w=$Win; $tr=$tray; $cfg=$Config
        $cm.Items.Clear()
        & $addMi '打开' ({ Show-MainWin $w }.GetNewClosure()) $null
        & $addMi '重新运行启动清单' ({ Invoke-LaunchSequenceAsync $cfg $tr }.GetNewClosure()) $null
        $gs=@($cfg.actionGroups)
        if ($gs.Count -gt 0) {
            & $addSep
            foreach($g in $gs){ (& $addMi ("运行：" + [string]$g.name) ({ param($s,$e) Invoke-ActionGroupAsync $s.Tag }.GetNewClosure()) ([bool]$g.enabled)).Tag = $g }
        }
        & $addSep
        & $addMi '暂停提醒 1 小时' ({ Set-WpfDnd $w $tr 1 }.GetNewClosure()) $null
        & $addMi '暂停提醒 2 小时' ({ Set-WpfDnd $w $tr 2 }.GetNewClosure()) $null
        & $addMi '暂停提醒 4 小时' ({ Set-WpfDnd $w $tr 4 }.GetNewClosure()) $null
        $d=$Win.Tag.Dnd.Until
        if($d -and (Get-Date) -lt $d){ & $addMi ("恢复提醒（剩 $([int][Math]::Ceiling(($d-(Get-Date)).TotalMinutes)) 分钟）") ({ $w.Tag.Dnd.Until=$null; Show-TrayNotify $tr '开机助手' '已恢复提醒' }.GetNewClosure()) $null }
        & $addSep
        & $addMi $(if($autostart.Registered){'取消开机自启'}else{'设为开机自启'}) $onAutoClick $null
        & $addSep
        & $addMi '退出' ({ Stop-WpfApp }.GetNewClosure()) $null
    }.GetNewClosure()

    # 右键托盘：重建菜单 → 置前台 → 在光标处打开。
    # 定位用 MousePoint（DPI 由 WPF 自己处理），不再手动换算：主窗从未显示过时（启动最小化 / -Run 自启）
    # 没有 PresentationSource，旧的「物理像素当 DIP」回退在高缩放屏上会把菜单开到屏幕外——表现为「右键菜单不出来」。
    $showTrayMenu = {
        try { $h=(New-Object System.Windows.Interop.WindowInteropHelper($Win)).EnsureHandle(); [void][SHFg.Win]::SetForegroundWindow($h) } catch {}
        $cm.IsOpen = $false        # 复位上次可能未正常收起的状态（IsOpen 残留 true 会吞掉本次打开）
        & $buildMenu
        $cm.HorizontalOffset = 0; $cm.VerticalOffset = 0
        $cm.Placement = [System.Windows.Controls.Primitives.PlacementMode]::MousePoint
        $cm.IsOpen = $true
    }.GetNewClosure()
    $tray.Add_MouseUp({ param($s,$e) if($e.Button -eq [System.Windows.Forms.MouseButtons]::Right){ $Win.Dispatcher.Invoke([action]$showTrayMenu) } }.GetNewClosure())
    $tray.Add_DoubleClick({ Show-MainWin $Win }.GetNewClosure())
    $Win.Tag | Add-Member -NotePropertyName Tray -NotePropertyValue $tray -Force
    $script:Tray=$tray; $tray
}

# 在后台 STA runspace 弹提醒（弹窗/朗读/onYes 都在那边跑），完成后回 UI 线程调 $OnDone(结果)。
# 关键：WPF 的 ShowDialog 是【线程级模态】——同线程的其它窗口全被禁用。提醒弹窗若在 UI 线程弹，
# 只要它开着（等点击/60 秒自动关），主窗口点什么都没反应，表现为「经常点一下就卡住」。
# 子 runspace 里 $script:AppRoot 未设 → Invoke-ActionGroupAsync/Start-SpeakAsync 自动走同步兜底（仍在后台线程，不碰 UI）。
function Invoke-ReminderAsync {
    param($Reminder, $Groups, [scriptblock]$OnDone)
    if (-not $script:AppRoot) { & $OnDone (Invoke-Reminder $Reminder $Groups); return }   # 测试/无根路径兜底
    # gJson 是动作组【数组】：@() 确保单组时也当集合序列化；读回时必须配 ConvertFrom-JsonArray 还原
    # （PS5.1 的 ConvertTo/From-Json 对数组两头都有折叠陷阱，见 ConvertFrom-JsonArray 注释）。
    Invoke-InRunspaceAsync -STA `
        -Vars @{ appRoot=$script:AppRoot; rJson=($Reminder | ConvertTo-Json -Depth 8); gJson=(ConvertTo-Json @($Groups) -Depth 8) } `
        -OnDone $OnDone `
        -Script {
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -AssemblyName System.Drawing
            Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
            . (Join-Path $appRoot 'lib\StartupHelper.Core.ps1')
            . (Join-Path $appRoot 'lib\StartupHelper.Win32.ps1')
            . (Join-Path $appRoot 'lib\StartupHelper.Actions.ps1')
            . (Join-Path $appRoot 'lib\StartupHelper.WpfDialogs.ps1')
            # 本 runspace 里静默组/点是组走【同步】Invoke-ActionGroup（AppRoot 故意不设）：音量/窗口步骤
            # 需要 SH.Audio/SH.Native，先编译，否则步骤静默失败且错误被吞。跨上下文防重入由组的命名互斥锁保证。
            Initialize-Win32Types
            $script:LaunchSelfPaths = @((Join-Path $appRoot 'startup-helper.ps1'), (Join-Path $appRoot '开机助手-双击运行.bat'))
            # 注意：动作组是数组，必须用 ConvertFrom-JsonArray 还原（PS5.1 下 @($gJson|ConvertFrom-Json)
            # 会把整组数组套成 1 个元素 → Resolve-ActionGroup 找不到组 → 点「是」运行动作组无反应）。
            $r = Invoke-Reminder ($rJson | ConvertFrom-Json) (ConvertFrom-JsonArray $gJson)
            Wait-SpeakDone   # 朗读未完前别让 runspace 释放（COM 随之释放会掐断语音）
            $r
        }
}

function Start-WpfReminderTimer {
    param($Win, $Config)
    $states=@{}; $firing=@{}; $startTime=Get-Date; $dnd=$Win.Tag.Dnd
    $uptimeAtLaunch = Get-SystemUptimeMinutes   # 程序启动那一刻的开机分钟数：给「登录时」提醒判定真开机 vs 白天重开
    $timer=New-Object System.Windows.Threading.DispatcherTimer
    $sec=30; if([int]$Config.settings.tickSeconds -gt 0){ $sec=[int]$Config.settings.tickSeconds }
    $timer.Interval=[TimeSpan]::FromSeconds($sec)
    $timer.Add_Tick({
        $now=Get-Date
        if($dnd.Until -and $now -lt $dnd.Until){ return }
        foreach($r in $Config.reminders){
            $key=[string]$r.id
            if(-not $states.ContainsKey($key)){ $states[$key]=New-ReminderState }
            $st=$states[$key]; $d=Get-ReminderDecision $r $now $startTime $st -UptimeMinutes $uptimeAtLaunch; $states[$key]=$d.state
            if($d.action -eq 'arm'){ $rand=if([int]$r.randomDelaySeconds -gt 0){Get-Random -Minimum 0 -Maximum ([int]$r.randomDelaySeconds+1)}else{0}; $d.state.pendingFireAt=$d.base.AddSeconds([int]$r.delaySeconds+$rand) }
            elseif($d.action -eq 'fire'){
                if($firing[$key]){ continue }   # 该提醒的弹窗还开着，不叠加
                $firing[$key]=$true
                # OnDone 是嵌套在本 tick 闭包里的闭包：GetNewClosure 只捕获【本层局部变量】，
                # $firing/$states 在 tick 闭包的模块作用域里、捕获不到（回调里成 $null →「不能对
                # Null 值表达式调用方法」，且 firing 标记永远清不掉=该提醒此后不再触发）。
                # 先取局部引用再建闭包，引用同一 hashtable，回调里的修改外层可见。
                $rr=$r; $f=$firing; $sts=$states
                Invoke-ReminderAsync $r $Config.actionGroups ({ param($out)
                    $f.Remove($key)
                    $p=$out | Select-Object -Last 1
                    if($null -eq $p){ return }   # 后台异常：不推进状态，下个周期照常
                    if($p.Action -eq 'snooze'){ $sts[$key]=Set-ReminderSnooze $sts[$key] (Get-Date) ([int]$p.SnoozeMinutes) }
                    else { $sts[$key]=Update-ReminderAfterFire $rr (Get-Date) ([string]$p.Action) $sts[$key] }
                }.GetNewClosure())
            }
        }
    }.GetNewClosure())
    $timer.Start(); $timer
}

# 后台枚举系统启动项。Invoke-InRunspaceAsync 的 OnDone 已在 UI 线程执行（轮询用的是 DispatcherTimer），
# 故直接把 $OnDone 透传，不再套一层 $disp.Invoke([action]{...})——那层会把结果数组二次包裹成 Object[1]{Object[]}。
function Start-SystemItemsAsyncWpf {
    param($Win, [scriptblock]$OnDone)
    if (-not $script:AppRoot) { & $OnDone (Get-SystemStartupItems); return }
    Invoke-InRunspaceAsync -Vars @{ appRoot = $script:AppRoot } -OnDone $OnDone -Script {
        . (Join-Path $appRoot 'lib\StartupHelper.Core.ps1'); . (Join-Path $appRoot 'lib\StartupHelper.SystemStartup.ps1')
        # 逐个 emit：Get-SystemStartupItems 返回的是「一个数组对象」，直接返回会被 EndInvoke 收成单元素→回调里
        # 只见 1 行(全部名字挤成一行)。foreach 把每项单独写出，输出流才是 N 个条目。
        foreach ($it in @(Get-SystemStartupItems)) { $it }
    }
}
