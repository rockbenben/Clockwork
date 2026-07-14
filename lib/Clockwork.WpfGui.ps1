# Clockwork.WpfGui.ps1 —— WPF 版界面（替代 WinForms 版 Gui.ps1）。逻辑层 Core/Actions/SystemStartup/Win32 复用。
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
        Title="Clockwork" Width="860" Height="720" MinWidth="680" MinHeight="520"
        WindowStartupLocation="CenterScreen" Background="#22262D" WindowStyle="None" ShowInTaskbar="True"
        FontFamily="Microsoft YaHei UI" TextOptions.TextFormattingMode="Display">
  <sh:WindowChrome.WindowChrome><sh:WindowChrome CaptionHeight="38" ResizeBorderThickness="6" CornerRadius="0" GlassFrameThickness="0"/></sh:WindowChrome.WindowChrome>
  <Window.Resources>
    <SolidColorBrush x:Key="Void" Color="#1A1D22"/><SolidColorBrush x:Key="Surface" Color="#22262D"/><SolidColorBrush x:Key="Raised" Color="#2A2F37"/>
    <SolidColorBrush x:Key="Ink" Color="#EAEDF1"/><SolidColorBrush x:Key="Muted" Color="#98A2AE"/><SolidColorBrush x:Key="Faint" Color="#6B7480"/>
    <SolidColorBrush x:Key="Signal" Color="#F0651A"/><SolidColorBrush x:Key="SignalHi" Color="#FF7C34"/>
    <SolidColorBrush x:Key="Panel" Color="#2A2F37"/><SolidColorBrush x:Key="Line" Color="#353C45"/><SolidColorBrush x:Key="Sel" Color="#382718"/>
    <SolidColorBrush x:Key="ChipBg" Color="#2E343C"/><SolidColorBrush x:Key="ChipInk" Color="#C7CDD5"/><SolidColorBrush x:Key="Destr" Color="#E0623E"/>

    <!-- 键盘焦点环：仅键盘 Tab 聚焦时显示橙色描边（鼠标点击不显），比默认虚线框清晰、贴暗色主题。 -->
    <Style x:Key="Focus"><Setter Property="Control.Template"><Setter.Value><ControlTemplate><Border BorderBrush="#FF7C34" BorderThickness="1.6" CornerRadius="9" Margin="-2" SnapsToDevicePixels="True"/></ControlTemplate></Setter.Value></Setter></Style>

    <Style TargetType="TabControl"><Setter Property="Background" Value="{StaticResource Surface}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="0"/></Style>
    <Style TargetType="TabItem">
      <Setter Property="FocusVisualStyle" Value="{StaticResource Focus}"/>
      <Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="FontSize" Value="14"/>
      <Setter Property="Template"><Setter.Value>
        <ControlTemplate TargetType="TabItem"><StackPanel Orientation="Horizontal">
          <!-- 分组细线：仅 Tag="sep" 的标签（系统启动项）显示，把「自动化三件套 | 系统工具」分开 -->
          <Rectangle x:Name="sep" Width="1" Fill="{StaticResource Line}" Margin="4,14" Visibility="Collapsed"/>
          <Border x:Name="bd" Background="Transparent" Padding="20,11,20,13" Cursor="Hand">
          <Grid><ContentPresenter ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center"/>
            <StackPanel x:Name="tick" Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,-9" Visibility="Collapsed">
              <Rectangle Width="2" Height="4" Fill="{StaticResource Signal}" Margin="1.5,0" Opacity="0.5"/><Rectangle Width="2" Height="5" Fill="{StaticResource Signal}" Margin="1.5,0" Opacity="0.75"/><Rectangle Width="2" Height="7" Fill="{StaticResource Signal}" Margin="1.5,0"/><Rectangle Width="2" Height="5" Fill="{StaticResource Signal}" Margin="1.5,0" Opacity="0.75"/><Rectangle Width="2" Height="4" Fill="{StaticResource Signal}" Margin="1.5,0" Opacity="0.5"/>
            </StackPanel></Grid></Border></StackPanel>
          <ControlTemplate.Triggers><Trigger Property="Tag" Value="sep"><Setter TargetName="sep" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="{StaticResource Ink}"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="tick" Property="Visibility" Value="Visible"/><Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="FontWeight" Value="Bold"/></Trigger>
          </ControlTemplate.Triggers></ControlTemplate>
      </Setter.Value></Setter>
    </Style>

    <Style TargetType="DataGrid">
      <Setter Property="Background" Value="{StaticResource Surface}"/><Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="BorderThickness" Value="0"/>
      <Setter Property="RowBackground" Value="{StaticResource Surface}"/><Setter Property="GridLinesVisibility" Value="Horizontal"/><Setter Property="HorizontalGridLinesBrush" Value="#2A3038"/>
      <Setter Property="HeadersVisibility" Value="Column"/><Setter Property="RowHeight" Value="42"/><Setter Property="FontSize" Value="13"/>
      <Setter Property="CanUserResizeRows" Value="False"/><Setter Property="SelectionMode" Value="Single"/><Setter Property="AutoGenerateColumns" Value="False"/><Setter Property="CanUserAddRows" Value="False"/>
      <!-- 禁用横向滚动：否则 * 列不为竖向滚动条预留宽度，最右列(右对齐的延时等)会被滚动条盖住。 -->
      <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
    </Style>
    <Style TargetType="DataGridRow"><Setter Property="Background" Value="Transparent"/>
      <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#262C33"/></Trigger>
        <DataTrigger Binding="{Binding C1}" Value="False"><Setter Property="Opacity" Value="0.48"/></DataTrigger>
      </Style.Triggers>
    </Style>
    <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="{StaticResource Surface}"/><Setter Property="Foreground" Value="{StaticResource Faint}"/><Setter Property="FontWeight" Value="SemiBold"/><Setter Property="FontSize" Value="12"/><Setter Property="BorderThickness" Value="0,0,0,1"/><Setter Property="BorderBrush" Value="{StaticResource Line}"/><Setter Property="Padding" Value="10,7"/><Setter Property="Height" Value="34"/></Style>
    <Style TargetType="DataGridCell"><Setter Property="BorderThickness" Value="0"/><Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="VerticalContentAlignment" Value="Center"/>
      <Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{StaticResource Sel}"/><Setter Property="Foreground" Value="{StaticResource Ink}"/></Trigger></Style.Triggers></Style>
    <!-- 文本列内容垂直居中：DataGridCell 的 VerticalContentAlignment 对 DataGridTextColumn 生成的 TextBlock 不生效，须经 ElementStyle 直接设 TextBlock。 -->
    <Style x:Key="CellText" TargetType="TextBlock"><Setter Property="VerticalAlignment" Value="Center"/><Setter Property="TextTrimming" Value="CharacterEllipsis"/></Style>
    <!-- 主文本列（唯一的 * 列）：超长省略号 + hover 显示全文，永不硬切；其余固定列也带省略号兜底 -->
    <Style x:Key="CellPrimary" TargetType="TextBlock"><Setter Property="VerticalAlignment" Value="Center"/><Setter Property="TextTrimming" Value="CharacterEllipsis"/><Setter Property="ToolTip" Value="{Binding Text, RelativeSource={RelativeSource Self}}"/></Style>
    <Style x:Key="CellMono" TargetType="TextBlock"><Setter Property="VerticalAlignment" Value="Center"/><Setter Property="FontFamily" Value="Consolas"/><Setter Property="FontSize" Value="12.5"/><Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="TextAlignment" Value="Right"/><Setter Property="Margin" Value="0,0,10,0"/></Style>
    <!-- 类型胶囊：只读展示，行禁用(C1=false)时随 DataGridRow 整体压暗 -->
    <Style x:Key="Chip" TargetType="Border"><Setter Property="Background" Value="{StaticResource ChipBg}"/><Setter Property="BorderBrush" Value="{StaticResource Line}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="CornerRadius" Value="7"/><Setter Property="Padding" Value="9,2"/><Setter Property="HorizontalAlignment" Value="Left"/><Setter Property="VerticalAlignment" Value="Center"/></Style>
    <Style x:Key="ChipText" TargetType="TextBlock"><Setter Property="Foreground" Value="{StaticResource ChipInk}"/><Setter Property="FontSize" Value="12.5"/></Style>

    <Style TargetType="CheckBox">
      <Setter Property="FocusVisualStyle" Value="{StaticResource Focus}"/>
      <Setter Property="Cursor" Value="Hand"/><Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="FontSize" Value="12"/>
      <Setter Property="Template"><Setter.Value>
        <ControlTemplate TargetType="CheckBox"><StackPanel Orientation="Horizontal" Background="Transparent">
          <Border x:Name="box" Width="18" Height="18" CornerRadius="5" Background="Transparent" BorderBrush="{StaticResource Faint}" BorderThickness="1.6" VerticalAlignment="Center">
            <Path x:Name="chk" Data="M4,9 L7.5,12.5 L14,5" Stroke="#1A1D22" StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" Stretch="None" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/></Border>
          <ContentPresenter Margin="8,0,0,0" VerticalAlignment="Center"/></StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True"><Setter TargetName="box" Property="Background" Value="{StaticResource Signal}"/><Setter TargetName="box" Property="BorderBrush" Value="{StaticResource Signal}"/><Setter TargetName="chk" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="{StaticResource Signal}"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers></ControlTemplate>
      </Setter.Value></Setter>
    </Style>
    <Style TargetType="TextBox"><Setter Property="Background" Value="{StaticResource Raised}"/><Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="BorderBrush" Value="{StaticResource Line}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="7,4"/><Setter Property="CaretBrush" Value="{StaticResource Ink}"/><Setter Property="FontSize" Value="13"/>
      <Style.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="BorderBrush" Value="{StaticResource Signal}"/></Trigger></Style.Triggers></Style>

    <Style x:Key="GroupLabel" TargetType="TextBlock"><Setter Property="Foreground" Value="{StaticResource Faint}"/><Setter Property="FontSize" Value="11"/><Setter Property="Margin" Value="2,0,0,6"/></Style>
    <Style x:Key="BtnGhost" TargetType="Button">
      <Setter Property="FocusVisualStyle" Value="{StaticResource Focus}"/>
      <Setter Property="Foreground" Value="{StaticResource Ink}"/><Setter Property="FontSize" Value="13"/><Setter Property="Height" Value="38"/><Setter Property="Margin" Value="0,0,0,8"/><Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="{StaticResource Raised}" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Signal}"/><Setter TargetName="b" Property="Background" Value="#31373F"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key="BtnPrimary" TargetType="Button" BasedOn="{StaticResource BtnGhost}"><Setter Property="Foreground" Value="#1A1D22"/><Setter Property="FontWeight" Value="Bold"/><Setter Property="Height" Value="40"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="{StaticResource Signal}" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="{StaticResource SignalHi}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key="BtnDestr" TargetType="Button" BasedOn="{StaticResource BtnGhost}"><Setter Property="Foreground" Value="{StaticResource Destr}"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="{StaticResource Raised}" BorderBrush="{StaticResource Line}" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Destr}"/><Setter TargetName="b" Property="Background" Value="#37211C"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key="BtnRun" TargetType="Button" BasedOn="{StaticResource BtnGhost}"><Setter Property="Foreground" Value="{StaticResource SignalHi}"/><Setter Property="Height" Value="40"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#2E2620" BorderBrush="#5A3E24" BorderThickness="1" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Signal}"/><Setter TargetName="b" Property="Background" Value="#37291C"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>

    <Style x:Key="Chrome" TargetType="Button"><Setter Property="Width" Value="46"/><Setter Property="Foreground" Value="{StaticResource Muted}"/><Setter Property="FontFamily" Value="Segoe MDL2 Assets"/><Setter Property="FontSize" Value="10"/><Setter Property="sh:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" Background="Transparent"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#3A4149"/><Setter Property="Foreground" Value="{StaticResource Ink}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 纤细滚动条：~10px、无箭头、圆角滑块，hover 变亮。级联到所有 DataGrid / 下拉 / 列表。 -->
    <Style x:Key="ScrollThumb" TargetType="Thumb"><Setter Property="OverridesDefaultStyle" Value="True"/><Setter Property="IsTabStop" Value="False"/><Setter Property="MinHeight" Value="28"/><Setter Property="MinWidth" Value="28"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Thumb"><Border x:Name="t" CornerRadius="3" Background="#464E58" Margin="2"/>
        <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="t" Property="Background" Value="#5C6672"/></Trigger><Trigger Property="IsDragging" Value="True"><Setter TargetName="t" Property="Background" Value="#6E7885"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
    <Style x:Key="ScrollPage" TargetType="RepeatButton"><Setter Property="OverridesDefaultStyle" Value="True"/><Setter Property="IsTabStop" Value="False"/><Setter Property="Focusable" Value="False"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></Setter.Value></Setter></Style>
    <Style TargetType="ScrollBar"><Setter Property="OverridesDefaultStyle" Value="True"/><Setter Property="Background" Value="Transparent"/><Setter Property="Width" Value="10"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Grid Background="Transparent"><Track x:Name="PART_Track" IsDirectionReversed="True">
        <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageUpCommand"/></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}"/></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageDownCommand"/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter>
      <Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="10"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Grid Background="Transparent"><Track x:Name="PART_Track" Orientation="Horizontal">
          <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageLeftCommand"/></Track.DecreaseRepeatButton>
          <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}"/></Track.Thumb>
          <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageRightCommand"/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Trigger></Style.Triggers></Style>
  </Window.Resources>

  <Grid>
    <Grid.RowDefinitions><RowDefinition Height="38"/><RowDefinition Height="*"/><RowDefinition Height="64"/></Grid.RowDefinitions>
    <Grid Grid.Row="0" Background="#1A1D22">
      <StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="12,0">
        <Viewbox Width="19" Height="19" VerticalAlignment="Center"><Canvas Width="256" Height="256"><Path Data="M99.68,59.63L107.68,40.32L148.32,40.32L156.32,59.63L156.32,59.63L175.63,51.63L204.37,80.37L196.37,99.68L196.37,99.68L215.68,107.68L215.68,148.32L196.37,156.32L196.37,156.32L204.37,175.63L175.63,204.37L156.32,196.37L156.32,196.37L148.32,215.68L107.68,215.68L99.68,196.37L99.68,196.37L80.37,204.37L51.63,175.63L59.63,156.32L59.63,156.32L40.32,148.32L40.32,107.68L59.63,99.68L59.63,99.68L51.63,80.37L80.37,51.63L99.68,59.63Z" Fill="#F0651A"/><Ellipse Canvas.Left="78" Canvas.Top="78" Width="100" Height="100" Fill="#1A1D22"/><Line X1="128" Y1="128" X2="164.4" Y2="107" Stroke="#F0651A" StrokeThickness="12" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/><Line X1="128" Y1="128" X2="103.8" Y2="114" Stroke="#F0651A" StrokeThickness="12" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/><Ellipse Canvas.Left="119" Canvas.Top="119" Width="18" Height="18" Fill="#FF7A2A"/></Canvas></Viewbox>
        <TextBlock Text="Clockwork" Foreground="{StaticResource Ink}" FontSize="13" FontWeight="SemiBold" Margin="9,0,0,0" VerticalAlignment="Center"/>
        <Border Width="1" Height="13" Background="{StaticResource Line}" Margin="10,1,0,0" VerticalAlignment="Center"/>
        <TextBlock Text="电脑上重复的事，自动帮你做" Foreground="{StaticResource Muted}" FontSize="11.5" Margin="10,1,0,0" VerticalAlignment="Center"/>
      </StackPanel>
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
        <Button x:Name="BtnMin" Style="{StaticResource Chrome}" Content="&#xE921;"/><Button x:Name="BtnMax" Style="{StaticResource Chrome}" Content="&#xE922;"/><Button x:Name="BtnClose" Style="{StaticResource Chrome}" Content="&#xE8BB;"/>
      </StackPanel>
    </Grid>

    <TabControl Grid.Row="1" x:Name="Tabs" Margin="10,4,10,10">
      <TabItem Header="我的启动清单">
        <Grid>
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
          <TextBlock Grid.Row="0" Text="开机时从上到下依次执行 · 每步可设「执行后延时」「重复次数」和「仅某些星期 / 仅 N 点前」" Foreground="{StaticResource Faint}" FontSize="12" Margin="2,10,0,4"/>
          <Grid Grid.Row="1"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="168"/></Grid.ColumnDefinitions>
            <DataGrid x:Name="GridLaunch" Grid.Column="0" Margin="0,4,0,0">
              <DataGrid.Columns>
                <DataGridTemplateColumn Header="启用" Width="52"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
                <DataGridTemplateColumn Header="类型" Width="110"><DataGridTemplateColumn.CellTemplate><DataTemplate><Border Style="{StaticResource Chip}"><TextBlock Text="{Binding T1}" Style="{StaticResource ChipText}"/></Border></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
                <DataGridTextColumn Header="摘要" Binding="{Binding T2}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellPrimary}"/>
                <DataGridTextColumn Header="延时" Binding="{Binding T3}" Width="92" IsReadOnly="True" ElementStyle="{StaticResource CellMono}"/>
              </DataGrid.Columns>
            </DataGrid>
            <TextBlock Grid.Column="0" Foreground="{StaticResource Faint}" FontSize="13" HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center" LineHeight="24" Text="还没有启动步骤。&#10;点右侧「新增 ▾」，加一个开机要做的事。"><TextBlock.Style><Style TargetType="TextBlock"><Setter Property="Visibility" Value="Collapsed"/><Style.Triggers><DataTrigger Binding="{Binding HasItems, ElementName=GridLaunch}" Value="False"><Setter Property="Visibility" Value="Visible"/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock>
            <StackPanel Grid.Column="1" Margin="14,4,0,0">
              <TextBlock Text="步骤" Style="{StaticResource GroupLabel}"/>
              <Button x:Name="LAdd" Content="新增 ▾" Style="{StaticResource BtnPrimary}"/>
              <Button x:Name="LEdit" Content="编辑" Style="{StaticResource BtnGhost}"/>
              <Button x:Name="LDel" Content="删除" Style="{StaticResource BtnDestr}"/>
              <TextBlock Text="排序" Style="{StaticResource GroupLabel}" Margin="2,10,0,6"/>
              <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="8"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Button x:Name="LUp" Grid.Column="0" Content="↑ 上移" Style="{StaticResource BtnGhost}"/>
                <Button x:Name="LDown" Grid.Column="2" Content="↓ 下移" Style="{StaticResource BtnGhost}"/></Grid>
              <TextBlock Text="试运行" Style="{StaticResource GroupLabel}" Margin="2,10,0,6"/>
              <Button x:Name="LTest" Content="▶ 运行这一步" Style="{StaticResource BtnRun}"/>
            </StackPanel>
          </Grid>
        </Grid>
      </TabItem>
      <TabItem Header="定时提醒">
        <Grid>
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
          <TextBlock Grid.Row="0" Text="到点提醒 · 可朗读 · 点「是」后运行程序 / 打开文件 / 开网页 / 跑动作组" Foreground="{StaticResource Faint}" FontSize="12" Margin="2,10,0,4"/>
          <Grid Grid.Row="1"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="168"/></Grid.ColumnDefinitions>
            <DataGrid x:Name="GridRemind" Grid.Column="0" Margin="0,4,0,0">
              <DataGrid.Columns>
                <DataGridTemplateColumn Header="启用" Width="52"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
                <DataGridTextColumn Header="时间" Binding="{Binding T1}" Width="106" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
                <DataGridTemplateColumn Header="周期" Width="120"><DataGridTemplateColumn.CellTemplate><DataTemplate><Border Style="{StaticResource Chip}"><TextBlock Text="{Binding T2}" Style="{StaticResource ChipText}"/></Border></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
                <DataGridTextColumn Header="文本" Binding="{Binding T3}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellPrimary}"/>
                <DataGridTemplateColumn Header="语音" Width="56"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C2, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C2" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
              </DataGrid.Columns>
            </DataGrid>
            <TextBlock Grid.Column="0" Foreground="{StaticResource Faint}" FontSize="13" HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center" LineHeight="24" Text="还没有提醒。&#10;点「新增」，设一个到点提醒。"><TextBlock.Style><Style TargetType="TextBlock"><Setter Property="Visibility" Value="Collapsed"/><Style.Triggers><DataTrigger Binding="{Binding HasItems, ElementName=GridRemind}" Value="False"><Setter Property="Visibility" Value="Visible"/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock>
            <StackPanel Grid.Column="1" Margin="14,4,0,0">
              <TextBlock Text="提醒" Style="{StaticResource GroupLabel}"/>
              <Button x:Name="RAdd" Content="新增" Style="{StaticResource BtnPrimary}"/>
              <Button x:Name="REdit" Content="编辑" Style="{StaticResource BtnGhost}"/>
              <Button x:Name="RDel" Content="删除" Style="{StaticResource BtnDestr}"/>
              <TextBlock Text="试运行" Style="{StaticResource GroupLabel}" Margin="2,10,0,6"/>
              <Button x:Name="RTest" Content="▶ 预览这条" Style="{StaticResource BtnRun}"/>
            </StackPanel>
          </Grid>
        </Grid>
      </TabItem>
      <TabItem Header="动作组">
        <Grid>
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
          <TextBlock Grid.Row="0" Text="把一串动作打包成组，托盘 / 开机清单 / 提醒里一键触发 ·「新增 ▾」可从常用模板建" Foreground="{StaticResource Faint}" FontSize="12" Margin="2,10,0,4"/>
          <Grid Grid.Row="1"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="168"/></Grid.ColumnDefinitions>
            <DataGrid x:Name="GridGroup" Grid.Column="0" Margin="0,4,0,0">
              <DataGrid.Columns>
                <DataGridTemplateColumn Header="启用" Width="52"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
                <DataGridTextColumn Header="名称" Binding="{Binding T1}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellPrimary}"/>
                <DataGridTextColumn Header="步骤" Binding="{Binding T2}" Width="90" IsReadOnly="True" ElementStyle="{StaticResource CellMono}"/>
              </DataGrid.Columns>
            </DataGrid>
            <TextBlock Grid.Column="0" Foreground="{StaticResource Faint}" FontSize="13" HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center" LineHeight="24" Text="还没有动作组。&#10;点「新增 ▾」，从常用模板建一个（专注 / 会议 / 收工…）。"><TextBlock.Style><Style TargetType="TextBlock"><Setter Property="Visibility" Value="Collapsed"/><Style.Triggers><DataTrigger Binding="{Binding HasItems, ElementName=GridGroup}" Value="False"><Setter Property="Visibility" Value="Visible"/></DataTrigger></Style.Triggers></Style></TextBlock.Style></TextBlock>
            <StackPanel Grid.Column="1" Margin="14,4,0,0">
              <TextBlock Text="动作组" Style="{StaticResource GroupLabel}"/>
              <Button x:Name="GAdd" Content="新增 ▾" Style="{StaticResource BtnPrimary}"/>
              <Button x:Name="GEdit" Content="编辑" Style="{StaticResource BtnGhost}"/>
              <Button x:Name="GDel" Content="删除" Style="{StaticResource BtnDestr}"/>
              <TextBlock Text="试运行" Style="{StaticResource GroupLabel}" Margin="2,10,0,6"/>
              <Button x:Name="GRun" Content="▶ 运行整组" Style="{StaticResource BtnRun}"/>
            </StackPanel>
          </Grid>
        </Grid>
      </TabItem>
      <TabItem Header="系统启动项" Tag="sep">
        <Grid><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <TextBlock Grid.Row="0" Text="电脑里所有开机自启，一键关掉不需要的 — 只禁用、不删除，随时恢复" Foreground="{StaticResource Faint}" FontSize="12" Margin="2,10,0,8"/>
          <Grid Grid.Row="1">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
              <TextBlock Text="过滤" Foreground="{StaticResource Muted}" VerticalAlignment="Center" FontSize="13" Margin="2,0,10,0"/>
              <TextBox x:Name="SSearch" Width="340" Height="32" VerticalContentAlignment="Center"/>
              <TextBlock Text="按名称或命令过滤" Foreground="{StaticResource Faint}" VerticalAlignment="Center" FontSize="12" Margin="12,0,0,0"/>
            </StackPanel>
            <!-- 只读的系统/策略/一次性/关键项默认隐藏（管不着、纯噪音）；勾此开关才显示。纯前端过滤、不改数据。 -->
            <CheckBox x:Name="ShowReadOnly" Content="显示系统 / 只读项" HorizontalAlignment="Right" VerticalAlignment="Center" Foreground="{StaticResource Muted}" FontSize="12.5" Cursor="Hand"/>
          </Grid>
          <DataGrid x:Name="GridSystem" Grid.Row="2" Margin="0,8,0,0">
            <DataGrid.Columns>
              <DataGridTemplateColumn Header="启用" Width="52"><DataGridTemplateColumn.CellTemplate><DataTemplate><CheckBox IsChecked="{Binding C1, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Tag="C1" IsEnabled="{Binding CanEdit}" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate></DataGridTemplateColumn.CellTemplate></DataGridTemplateColumn>
              <DataGridTextColumn Header="名称" Binding="{Binding T1}" Width="196" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="命令" Binding="{Binding T2}" Width="*" IsReadOnly="True" ElementStyle="{StaticResource CellPrimary}"/>
              <DataGridTextColumn Header="来源" Binding="{Binding T3}" Width="130" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
              <DataGridTextColumn Header="范围" Binding="{Binding T4}" Width="132" IsReadOnly="True" ElementStyle="{StaticResource CellText}"/>
            </DataGrid.Columns>
          </DataGrid>
          <!-- 扫描态：枚举系统自启需要读多处注册表/目录，首帧空白会像“没有项”。放一句“正在扫描…”盖在表格上，由代码在开始/结束时切换。 -->
          <TextBlock x:Name="SysLoading" Grid.Row="2" Text="正在扫描开机自启项…" Foreground="{StaticResource Muted}" FontSize="13" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
          <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,12,0,0">
            <Button x:Name="SRefresh" Content="刷新" Style="{StaticResource BtnGhost}" Width="104" Height="34" Margin="0,0,10,0"/>
            <Button x:Name="SImport" Content="纳入启动清单" Style="{StaticResource BtnGhost}" Width="140" Height="34" Margin="0"/>
          </StackPanel>
          <TextBlock x:Name="SHint" Grid.Row="4" Text="勾选 / 取消即时生效（非删除，可恢复）；标「需管理员」的项需管理员身份。系统 / 策略 / 一次性等【只读项默认隐藏】，勾右上角「显示系统 / 只读项」可查看（它们无法开关，纯供了解）。" Foreground="{StaticResource Faint}" TextWrapping="Wrap" Margin="2,10,0,0" FontSize="12"/>
        </Grid>
      </TabItem>
    </TabControl>

    <Border Grid.Row="2" Background="#1A1D22" BorderBrush="{StaticResource Line}" BorderThickness="0,1,0,0">
      <Grid Margin="16,0">
        <Button x:Name="BtnRun" HorizontalAlignment="Left" VerticalAlignment="Center" Height="42" Width="224" Foreground="#1A1D22" FontWeight="Bold" FontSize="14.5" Cursor="Hand" FocusVisualStyle="{StaticResource Focus}">
          <Button.Template><ControlTemplate TargetType="Button"><Border x:Name="b" Background="#F0651A" CornerRadius="8"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
            <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#FF7C34"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Button.Template>
          <TextBlock Text="▶  重新运行启动清单"/>
        </Button>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
          <TextBlock Text="开机延迟" Foreground="{StaticResource Muted}" FontSize="12.5" VerticalAlignment="Center"
                     ToolTip="仅开机自启时生效：登录后固定等这么多秒、让登录风暴过峰，再跑启动清单。手动「重新运行」不受影响。开得太早/程序没打开就把它调大。"/>
          <TextBox x:Name="TxtDelay" Width="48" Height="30" Margin="8,0,4,0" Padding="2,1" FontFamily="Consolas" TextAlignment="Center" FontSize="13" VerticalContentAlignment="Center"
                   MaxLength="3" ToolTip="0–600 秒。开机自启后固定等待的秒数（唯一的延时杠杆，不够就往大调）。"/>
          <TextBlock Text="秒" Foreground="{StaticResource Muted}" FontSize="12.5" VerticalAlignment="Center" Margin="0,0,22,0"/>
          <TextBlock Text="急停键" Foreground="{StaticResource Muted}" FontSize="12.5" VerticalAlignment="Center"
                     ToolTip="全局快捷键：随时停止正在运行的启动清单 / 动作组 / 单步运行（循环动作跑飞时的刹车）。托盘右键菜单里也有「停止」。"/>
          <TextBox x:Name="TxtStopKey" Width="104" Height="30" Margin="8,0,22,0" Padding="2,1" FontFamily="Consolas" TextAlignment="Center" FontSize="12.5" VerticalContentAlignment="Center"
                   ToolTip="如 Ctrl+Alt+F12（支持 Ctrl/Alt/Shift/Win + 字母/数字/F1-F12 等）。清空 = 禁用。点别处生效。"/>
          <CheckBox x:Name="ChkMin" Content="启动时最小化到托盘" VerticalAlignment="Center" Foreground="{StaticResource Muted}" FontSize="12.5" Cursor="Hand"/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Window>
'@

# 延时列人话化：0 → 「—」；整秒 → 「N 秒」；否则 1 位小数秒（800→0.8 秒、1500→1.5 秒）。列表显示用（原始 ms 仍存配置）。
function Format-DelayShort {
    param([int]$Ms)
    if ($Ms -le 0) { return '—' }
    if ($Ms % 1000 -eq 0) { return "$([int]($Ms/1000)) 秒" }
    "$([math]::Round($Ms/1000.0, 1)) 秒"
}

# 把启动步骤投影成显示行（ShRow）。启用勾选回写 step.enabled 并保存。
function Get-LaunchRows {
    param($Config)
    $rows = New-Object System.Collections.ObjectModel.ObservableCollection[object]
    foreach ($st in @($Config.launchSteps)) {
        $r = New-Object ShRow
        $r.C1 = [bool]$st.enabled
        $r.T1 = Get-StepKindLabel $st.kind
        $r.T2 = Format-StepListSummary $st
        $r.T3 = Format-DelayShort ([int]$st.delayMs)
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
        $sum = Format-Ellipsis ([string]$r.message -replace "`r?`n",' ')
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
            else { Invoke-ActionGroupAsync $g (Get-StepRepeat $st) ([int]$st.delayMs) }   # 预览按该 group 步骤的 repeat/delay 循环整组（原来恒跑一遍，循环组步骤的测试失真）。注：预览会弹组内消息步骤，开机序列则跳过
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

    # 急停键：失焦提交。解析不了/注册被拒（与其他程序冲突）→ 提示并回滚旧值（旧热键保持生效）；成功才落盘。
    $txtStop = $win.FindName('TxtStopKey')
    $txtStop.Text = [string]$Config.settings.stopHotkey
    $commitStop = {
        $v = $txtStop.Text.Trim()
        $old = [string]$Config.settings.stopHotkey
        if ($v -eq $old) { $txtStop.Text = $old; return }
        # 单一分类来源：Register-StopHotkey 自身返回状态字（受限/格式/占用/Ok），不再事后重跑判断猜原因。
        $st = Register-StopHotkey $win $win.Tag.Tray $v
        if ($st -ne 'Ok') {
            [System.Windows.MessageBox]::Show("快捷键「$v」$(Get-StopHotkeyStatusMessage $st)，请换一个。",'急停键')|Out-Null
            $txtStop.Text = $old; [void](Register-StopHotkey $win $win.Tag.Tray $old); return
        }
        $Config.settings.stopHotkey = $v; Save-Config
    }.GetNewClosure()
    $txtStop.Add_LostFocus($commitStop)

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
    $win.FindName('GRun').Add_Click({ $i=& $selG; if($i -ge 0){ $g=$Config.actionGroups[$i]; if(-not $g.enabled){ [System.Windows.MessageBox]::Show("动作组「$($g.name)」已禁用，请先启用。",'Clockwork')|Out-Null; return }; Invoke-ActionGroupAsync $g } }.GetNewClosure())

    # —— 系统启动项 Tab（首次进入懒加载，后台枚举）——
    $gs = $win.FindName('GridSystem')
    $ss = $win.FindName('SSearch')
    $sysLoading = $win.FindName('SysLoading')
    $showReadOnly = $win.FindName('ShowReadOnly')
    Add-CheckClickSelect $gs   # 点勾选框即选中该行（「纳入启动清单」按 SelectedItem 取行，否则会对错行操作）
    $sysState = @{ Loaded=$false; Loading=$false; AllRows=$null }
    # 搜索/过滤：按名称(T1)或命令(T2)不区分大小写过滤；过滤后 ItemsSource 仍用同一批 ShRow（保住 OnC1/勾选态）。
    # 用 IndexOf 不用通配/正则，含 [ 等字符也不炸。
    # 只读项（CanEdit=false：策略/一次性/系统关键/Active Setup，管不着的噪音）默认隐藏，勾「显示系统/只读项」才显示；
    # 标签带上隐藏计数，让人知道是被收起、并非列表不全。纯前端过滤、不改数据、不动 Clockwork 自身（它 CanEdit=true，照常显示）。
    $applySysFilter = {
        if ($null -eq $sysState.AllRows) { return }
        $q = $ss.Text.Trim(); $showRO = [bool]$showReadOnly.IsChecked; $hidden = 0
        $view = New-Object System.Collections.ObjectModel.ObservableCollection[object]
        foreach ($r in $sysState.AllRows) {
            $match = (-not $q -or ([string]$r.T1).IndexOf($q,[System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or ([string]$r.T2).IndexOf($q,[System.StringComparison]::OrdinalIgnoreCase) -ge 0)
            if (-not $r.CanEdit -and -not $showRO) { if ($match) { $hidden++ }; continue }   # 隐藏计数只算能匹配当前搜索的只读项，否则输入过滤词时会虚高
            if ($match) { $view.Add($r) }
        }
        $gs.ItemsSource = $view
        $showReadOnly.Content = if ($hidden -gt 0) { "显示系统 / 只读项（$hidden）" } else { '显示系统 / 只读项' }
    }.GetNewClosure()
    $ss.Add_TextChanged($applySysFilter)
    $showReadOnly.Add_Click($applySysFilter)
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
        $sysLoading.Visibility = 'Collapsed'
    }.GetNewClosure()
    $loadSys = {
        if ($sysState.Loading) { return }
        $sysState.Loading = $true; $gs.ItemsSource = $null; $sysLoading.Visibility = 'Visible'
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
    # 系统启动项已挪到第 4 位（index 3）：懒加载触发的选中索引随之从 2 改为 3，否则进该 tab 不会扫描。
    $win.FindName('Tabs').Add_SelectionChanged({ param($s,$e) if ($e.OriginalSource -is [System.Windows.Controls.TabControl] -and $s.SelectedIndex -eq 3 -and -not $sysState.Loaded) { & $loadSys } }.GetNewClosure())

    $win.Tag = [pscustomobject]@{ Config=$Config; ReloadLaunch=$reloadLaunch; ReloadGroup=$reloadGroup; Dnd=@{ Until=$null }; ReallyExit=$false }
    $script:MainWin = $win
    $win
}

# —— 全局急停快捷键（停止所有正在运行的动作）——
# RegisterHotKey + 主窗 HWND 的 WndProc 钩子。EnsureHandle：窗口无需显示过也能拿句柄（-Run/启动最小化
# 只入托盘的路径同样生效）。settings.stopHotkey 空=禁用。设置变更时重复调用即重注册（同一 id 先注销再注册）。
# 返回【状态字】而非裸 bool——由函数内部【实际命中的失败分支】直接给出，调用方不再事后重跑同样的判断去猜原因
# （那会两处分类漂移，正是「受限环境被误报占用」的成因）。取值：'Ok'（注册成功或空=禁用）/ 'Restricted'（受限令牌
# 编译不出 P/Invoke，热键降级、托盘「停止」仍可用）/ 'BadFormat'（键名解析不出）/ 'Occupied'（RegisterHotKey 被拒，多半被占用）。
$script:StopHotkeyId = 0xB0B
function Register-StopHotkey {
    param($Win, $Tray, [string]$Combo)
    # 独立小段 P/Invoke（SHHk）而非并入 SH.Native：后者刻意懒编译（Confirm-Win32Available），
    # 注册热键发生在 GUI 启动路径、不该为它提前触发那次 ~200ms 的 csc 编译。
    try { Add-Type -Namespace SHHk -Name Win -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(System.IntPtr hWnd, int id, uint fsModifiers, uint vk); [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool UnregisterHotKey(System.IntPtr hWnd, int id);' -ErrorAction Stop } catch {}
    $native = [bool]('SHHk.Win' -as [type])
    # 空=禁用：先处理（不受「受限」影响）——能注销就注销既有，视作成功。受限环境下本就没注册过，直接 Ok。
    if ([string]::IsNullOrWhiteSpace($Combo)) {
        if ($native) { try { $h=(New-Object System.Windows.Interop.WindowInteropHelper($Win)).EnsureHandle(); [void][SHHk.Win]::UnregisterHotKey($h, $script:StopHotkeyId) } catch {} }
        return 'Ok'
    }
    if (-not $native) { return 'Restricted' }   # 受限令牌下编译失败：热键功能降级，托盘「停止」仍可用
    $hp = ConvertTo-HotkeyParams $Combo
    if (-not $hp) { return 'BadFormat' }
    $script:StopHkTray = $Tray   # 钩子回调经 $script: 取（不走 GetNewClosure——闭包模块作用域取不到 script 变量，见托盘处注释）
    $h = (New-Object System.Windows.Interop.WindowInteropHelper($Win)).EnsureHandle()
    try { [void][SHHk.Win]::UnregisterHotKey($h, $script:StopHotkeyId) } catch {}   # 重注册：先注销旧的；从未注册过则无害失败
    if (-not $script:StopHkHooked) {
        $src = [System.Windows.Interop.HwndSource]::FromHwnd($h)
        $src.AddHook([System.Windows.Interop.HwndSourceHook]{
            param($hwnd, $msg, $wParam, $lParam, [ref]$handled)
            if ($msg -eq 0x0312 -and $wParam.ToInt64() -eq $script:StopHotkeyId) {   # WM_HOTKEY
                Request-StopAll
                if ($script:StopHkTray) { Show-TrayNotify $script:StopHkTray 'Clockwork · 急停' '已停止所有正在运行的动作（急停键）' 'stop' }
                $handled.Value = $true
            }
            [IntPtr]::Zero
        })
        $script:StopHkHooked = $true
    }
    # MOD_NOREPEAT(0x4000)：按住不松不连发
    if ([SHHk.Win]::RegisterHotKey($h, $script:StopHotkeyId, ($hp.Modifiers -bor 0x4000), $hp.Vk)) { 'Ok' } else { 'Occupied' }
}

# 状态字 → 用户可读原因。措辞集中一处，启动路径与 GUI 改键路径共用，绝不漂移。
function Get-StopHotkeyStatusMessage {
    param([string]$Status)
    switch ($Status) {
        'Restricted' { '当前环境受限，无法注册全局热键（托盘「停止」仍可用）' }
        'BadFormat'  { '格式无法识别' }
        'Occupied'   { '注册失败（多半被其他程序占用）' }
        default      { '' }
    }
}

# NeedsAdmin 统一处理：未提权 → 询问「以管理员身份重开」（README 承诺的流程）；已提权仍失败 → 如实报权限不足。
# 系统启动项勾选、纳入清单、开机自启注册三处共用。
function Show-NeedsAdminPrompt {
    param([string]$What)
    if (Test-IsElevated) { [System.Windows.MessageBox]::Show("操作失败：权限不足（$What）。",'Clockwork')|Out-Null; return }
    $r = [System.Windows.MessageBox]::Show("「$What」需要管理员权限。`n是否以管理员身份重新打开本程序？",'需要管理员权限',[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Question)
    if ($r -eq 'Yes') { if (Restart-Elevated $script:SelfPath) { Stop-WpfApp } }
}

function Show-MainWin { param($Win) $Win.Show(); if ($Win.WindowState -eq 'Minimized') { $Win.WindowState='Normal' }; $Win.ShowInTaskbar=$true; [void]$Win.Activate() }
function Stop-WpfApp { try { $script:MainWin.Tag.ReallyExit=$true } catch {}; try { if ($script:Tray) { $script:Tray.Visible=$false } } catch {}; try { [System.Windows.Application]::Current.Shutdown() } catch { try { $script:MainWin.Dispatcher.InvokeShutdown() } catch {} } }
function Set-WpfDnd { param($Win, $Tray, [int]$Hours) $Win.Tag.Dnd.Until=(Get-Date).AddHours($Hours); Show-TrayNotify $Tray 'Clockwork' "已暂停提醒 $Hours 小时" }

function Add-WpfTray {
    param($Win, $Config)
    $self = $script:SelfPath
    $appRoot = $script:AppRoot
    $autostart = @{ Registered=$false; UserSet=$false; Busy=$false; Pending=$false }
    # 注册/注销开机自启完成后回 UI 线程处理结果（单层闭包，捕获 $autostart/$self；避免嵌套闭包捕获失败）。
    $onAutoDone = { param($out)
        $autostart.Busy=$false; $reg=[bool]$autostart.Pending; $res=[string]($out|Select-Object -Last 1)
        if($res -eq 'Ok'){ $autostart.Registered=$reg; $autostart.UserSet=$true; [System.Windows.MessageBox]::Show($(if($reg){'已注册为登录时自启（最高权限）。'}else{'已取消开机自启。'}),'Clockwork')|Out-Null }
        elseif($res -eq 'NeedsAdmin'){ Show-NeedsAdminPrompt $(if($reg){'设为开机自启'}else{'取消开机自启'}) }
        else { [System.Windows.MessageBox]::Show("开机自启操作失败：`n$res",'Clockwork')|Out-Null }
    }.GetNewClosure()
    if ($appRoot) {
        Invoke-InRunspaceAsync -Vars @{ appRoot=$appRoot } -OnDone ({ param($out) if(-not $autostart.UserSet){ $autostart.Registered=[bool]($out|Select-Object -Last 1) } }.GetNewClosure()) -Script { . (Join-Path $appRoot 'lib\Clockwork.Core.ps1'); . (Join-Path $appRoot 'lib\Clockwork.Actions.ps1'); [bool](Test-AutostartRegistered) }
    }
    $tray = New-Object System.Windows.Forms.NotifyIcon
    $tray.Text='Clockwork'; $ic=Get-AppIcon; $tray.Icon = if($ic){$ic}else{[System.Drawing.SystemIcons]::Application}; $tray.Visible=$true

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
        Show-TrayNotify $tray 'Clockwork' '正在修改开机自启…'
        Invoke-InRunspaceAsync -Vars @{ appRoot=$appRoot; scriptPath=$self; reg=$autostart.Pending } -OnDone $onAutoDone -Script {
            . (Join-Path $appRoot 'lib\Clockwork.Core.ps1'); . (Join-Path $appRoot 'lib\Clockwork.Win32.ps1'); . (Join-Path $appRoot 'lib\Clockwork.Actions.ps1')
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
        # 急停：置全局停止信号，正在跑的启动清单/动作组/单步在当前动作后停下（菜单项常驻——没在跑时按下无害）。
        $hkTxt = [string]$cfg.settings.stopHotkey
        & $addMi $(if($hkTxt){"停止正在运行的动作（$hkTxt）"}else{'停止正在运行的动作'}) ({ Request-StopAll; Show-TrayNotify $tr 'Clockwork · 急停' '已停止所有正在运行的动作' 'stop' }.GetNewClosure()) $null
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
        if($d -and (Get-Date) -lt $d){ & $addMi ("恢复提醒（剩 $([int][Math]::Ceiling(($d-(Get-Date)).TotalMinutes)) 分钟）") ({ $w.Tag.Dnd.Until=$null; Show-TrayNotify $tr 'Clockwork' '已恢复提醒' }.GetNewClosure()) $null }
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
            . (Join-Path $appRoot 'lib\Clockwork.Core.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Win32.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.Actions.ps1')
            . (Join-Path $appRoot 'lib\Clockwork.WpfDialogs.ps1')
            # 本 runspace 里静默组/点是组走【同步】Invoke-ActionGroup（AppRoot 故意不设）：音量/窗口步骤
            # 需要 SH.Audio/SH.Native，先编译，否则步骤静默失败且错误被吞。跨上下文防重入由组的命名互斥锁保证。
            Initialize-Win32Types
            $script:LaunchSelfPaths = @((Join-Path $appRoot 'clockwork.ps1'), (Join-Path $appRoot 'Clockwork.bat'))
            # 注意：动作组是数组，必须用 ConvertFrom-JsonArray 还原（PS5.1 下 @($gJson|ConvertFrom-Json)
            # 会把整组数组套成 1 个元素 → Resolve-ActionGroup 找不到组 → 点「是」运行动作组无反应）。
            $r = Invoke-Reminder ($rJson | ConvertFrom-Json) (ConvertFrom-JsonArray $gJson)
            Wait-SpeakDone   # 朗读未完前别让 runspace 释放（COM 随之释放会掐断语音）
            $r
        }
}

function Start-WpfReminderTimer {
    param($Win, $Config)
    # $script:ReminderFiring：主 runspace 维护的「提醒在弹/在跑」表（派发时置、OnDone 移除）。提醒在背景 runspace
    # 里跑、不设 Launch/Step/Group 守卫，Test-AnyRunActive 靠读它才能把「提醒在跑」算进「有运行」——故必须 $script 域
    # （供 Actions.ps1 的 Test-AnyRunActive 在同一主 runspace 读到），不能是本函数局部。
    $script:ReminderFiring=@{}; $firing=$script:ReminderFiring
    $states=@{}; $startTime=Get-Date; $dnd=$Win.Tag.Dnd
    $uptimeAtLaunch = Get-SystemUptimeMinutes   # 程序启动那一刻的开机分钟数：给「登录时」提醒判定真开机 vs 白天重开
    $timer=New-Object System.Windows.Threading.DispatcherTimer
    $sec=30; if([int]$Config.settings.tickSeconds -gt 0){ $sec=[int]$Config.settings.tickSeconds }
    $timer.Interval=[TimeSpan]::FromSeconds($sec)
    $timer.Add_Tick({
        $now=Get-Date
        # 心跳清理滞留的空闲急停：急停信号全局粘滞，空闲时按下（或运行结束后残留）若不清，会静默压制之后每个
        # 静默/点是动作组提醒（背景 runspace 里的组会看到旧信号而直接中止）。Test-AnyRunActive 现已覆盖提醒在跑
        # （$script:ReminderFiring）+ 主线程启动/单步/UI组，故仅它为假即真空闲、可安全清；在 UI 线程做，守卫态权威。
        if (-not (Test-AnyRunActive) -and (Test-StopRequested)) { Clear-StopAll }
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
                # 同步派发失败（Invoke-ReminderAsync 里 ConvertTo-Json、或建 runspace 早于计时器起来即抛）会跳过 OnDone，
                # 让 $script:ReminderFiring[$key] 永久滞留——而它现在门控急停信号的清理（Test-AnyRunActive），一处滞留就把急停
                # 永久卡死。try/catch 兜底：同步抛错即移除该标志、如实告警。（Invoke-InRunspaceAsync 内部的建 runspace 抛错另有兜底。）
                try {
                    Invoke-ReminderAsync $r $Config.actionGroups ({ param($out)
                        $f.Remove($key)
                        $p=$out | Select-Object -Last 1
                        if($null -eq $p){ return }   # 后台异常：不推进状态，下个周期照常
                        if($p.Action -eq 'snooze'){ $sts[$key]=Set-ReminderSnooze $sts[$key] (Get-Date) ([int]$p.SnoozeMinutes) }
                        else { $sts[$key]=Update-ReminderAfterFire $rr (Get-Date) ([string]$p.Action) $sts[$key] }
                    }.GetNewClosure())
                } catch { $f.Remove($key); Write-Warning "提醒触发失败：$($_.Exception.Message)" }
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
        . (Join-Path $appRoot 'lib\Clockwork.Core.ps1'); . (Join-Path $appRoot 'lib\Clockwork.SystemStartup.ps1')
        # 逐个 emit：Get-SystemStartupItems 返回的是「一个数组对象」，直接返回会被 EndInvoke 收成单元素→回调里
        # 只见 1 行(全部名字挤成一行)。foreach 把每项单独写出，输出流才是 N 个条目。
        foreach ($it in @(Get-SystemStartupItems)) { $it }
    }
}
