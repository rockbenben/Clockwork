// WPF + WinForms 互操作后，同名控件在两命名空间间歧义。本应用以 WPF 为主，WinForms 仅在 TrayIcon /
// SystemCommands 里显式限定（WinForms.* / System.Windows.Forms.*）。故这些常用类型全局别名指向 WPF。
global using ComboBox = System.Windows.Controls.ComboBox;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using MenuItem = System.Windows.Controls.MenuItem;
global using TabControl = System.Windows.Controls.TabControl;
global using MessageBox = System.Windows.MessageBox;
