using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Windows.Storage.Pickers;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ToolboxWinUI.Controls;
using ToolboxWinUI.Models;
using Windows.Foundation;
using WColor = Windows.UI.Color;
using Path = System.IO.Path;
using Window = Microsoft.UI.Xaml.Window;

namespace ToolboxWinUI;

public sealed partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int GWLP_WNDPROC = -4;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int HTCAPTION = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private IntPtr _oldWndProc;
    private WndProcDelegate _wndProcDelegate;

    private readonly SolidColorBrush _textPrimaryBrush = new(WColor.FromArgb(255, 30, 30, 30));
    private readonly SolidColorBrush _textSecondaryBrush = new(WColor.FromArgb(255, 100, 100, 100));
    private readonly SolidColorBrush _cardBgBrush = new(WColor.FromArgb(255, 255, 255, 255));
    private readonly SolidColorBrush _cardBorderBrush = new(WColor.FromArgb(255, 220, 220, 220));

    // 系统信息缓存
    private string _cpuInfo, _cpuCores, _cpuUsage, _cpuFreq;
    private string _totalMemory, _availableMemory, _memoryUsage, _memoryFreq;
    private string _gpuInfo, _gpuMemory;
    private string _diskInfo, _diskModel, _osInfo, _localIP, _audioInfo, _monitorInfo;
    private UIElement _systemInfoCache;
    private bool _loaded;

    // 图表
    private DispatcherQueueTimer _chartTimer;
    private PerformanceCounter _cpuCounter;
    private List<PerformanceCounter> _gpuCounters;

    // 工具数据 & 收藏
    private List<ToolInfo> _allTools = [];
    private HashSet<string> _favorites = [];
    private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToolboxWinUI");
    private static readonly string FavoritesFile = Path.Combine(DataDir, "favorites.json");
    private static readonly string ToolsFile = Path.Combine(DataDir, "tools.json");

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);

        // 锁定窗口最小尺寸
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 600;
        }

        ShowVersion();
        InitSystemInfo();
        LoadData();
        DisableDoubleClickMaximize();

        _cpuCounter = TryCreateCpuCounter();
        App.ThemeChanged += OnThemeChanged;
        Closed += (s, e) => Cleanup();

        Activated += async (s, e) =>
        {
            if (_loaded) return;
            _loaded = true;
            await Task.Run(() =>
            {
                _cpuInfo = GetCPUInfo();
                _cpuCores = GetCPUCores();
                _cpuUsage = GetCPUUsage();
                _cpuFreq = GetCPUFreq();
                _totalMemory = GetTotalMemory();
                _availableMemory = GetAvailableMemory();
                _memoryUsage = GetMemoryUsage();
                _memoryFreq = GetMemoryFreq();
                _gpuInfo = GetGPUInfo();
                _gpuMemory = GetGPUMemory();
                _diskInfo = GetDiskInfo();
                _diskModel = GetDiskModel();
                _osInfo = GetOSInfo();
                _localIP = GetLocalIPAddress();
                _audioInfo = GetAudioInfo();
                _monitorInfo = GetMonitorInfo();
            });
            _systemInfoCache = null;
            LoadContent("system");
        };
    }

    #region 应用生命周期

    private void ShowVersion()
    {
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null)
                versionText.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
        }
        catch { }
    }

    private void InitSystemInfo()
    {
        _cpuInfo = "查询中...";
        _totalMemory = "查询中...";
        _gpuInfo = "查询中...";
        _cpuFreq = "查询中...";
        _memoryFreq = "查询中...";
    }

    private void LoadData()
    {
        try { Directory.CreateDirectory(DataDir); } catch { }
        LoadFavorites();
        LoadTools();
    }

    private string _currentTag = "system";

    private void Cleanup()
    {
        _chartTimer?.Stop();
        _cpuCounter?.Dispose();
        if (_gpuCounters != null)
            foreach (var pc in _gpuCounters) pc.Dispose();
        App.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                bool glass = App.CurrentTheme == "DarkGlass" || App.CurrentTheme == "LightGlass";
                bool darkGlass = App.CurrentTheme == "DarkGlass";
                bool solidDark = App.CurrentTheme == "Dark";

                if (glass)
                {
                    SystemBackdrop = new DesktopAcrylicBackdrop();
                    rootGrid.RequestedTheme = darkGlass ? ElementTheme.Dark : ElementTheme.Light;
                    _textPrimaryBrush.Color = darkGlass
                        ? WColor.FromArgb(255, 224, 224, 224)
                        : WColor.FromArgb(255, 30, 30, 30);
                    _textSecondaryBrush.Color = darkGlass
                        ? WColor.FromArgb(255, 156, 156, 156)
                        : WColor.FromArgb(255, 100, 100, 100);
                    _cardBgBrush.Color = darkGlass
                        ? WColor.FromArgb(160, 30, 30, 30)
                        : WColor.FromArgb(100, 248, 248, 248);
                    _cardBorderBrush.Color = darkGlass
                        ? WColor.FromArgb(0, 0, 0, 0)
                        : WColor.FromArgb(60, 150, 150, 150);
                }
                else
                {
                    SystemBackdrop = null;
                    rootGrid.RequestedTheme = ElementTheme.Default;
                    _textPrimaryBrush.Color = WColor.FromArgb(255, 30, 30, 30);
                    _textSecondaryBrush.Color = WColor.FromArgb(255, 100, 100, 100);
                    _cardBgBrush.Color = WColor.FromArgb(255, 255, 255, 255);
                    _cardBorderBrush.Color = WColor.FromArgb(255, 220, 220, 220);
                }

                // 使用 DWM API 设置深色标题栏
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                int value = darkGlass ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));

                _systemInfoCache = null;
                // 设置页面不自动跳转
                if (contentArea.Children.Count > 0 && contentArea.Children[0] is Pages.SettingsPage)
                    return;
                LoadContent(_currentTag);
            }
            catch { }
        });
    }

    #endregion

    #region 收藏 & 工具数据

    private void LoadFavorites()
    {
        try
        {
            if (File.Exists(FavoritesFile))
                _favorites = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FavoritesFile)) ?? [];
        }
        catch { }
    }

    private void SaveFavorites()
    {
        try { File.WriteAllText(FavoritesFile, JsonSerializer.Serialize(_favorites)); } catch { }
    }

    private void LoadTools()
    {
        try
        {
            if (File.Exists(ToolsFile))
            {
                _allTools = JsonSerializer.Deserialize<List<ToolInfo>>(File.ReadAllText(ToolsFile)) ?? [];
                MergeScannedTools();
                return;
            }
        }
        catch { }

        var localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools.json");
        try
        {
            if (File.Exists(localFile))
            {
                _allTools = JsonSerializer.Deserialize<List<ToolInfo>>(File.ReadAllText(localFile)) ?? [];
                MergeScannedTools();
                return;
            }
        }
        catch { }

        InitDefaultTools();
    }

    private void MergeScannedTools()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var toolsDir = Path.Combine(baseDir, "tools");
        if (!Directory.Exists(toolsDir)) return;

        // 收集当前 tools 目录中所有存在的可执行文件路径
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var catDir in Directory.GetDirectories(toolsDir))
        {
            foreach (var toolDir in Directory.GetDirectories(catDir))
            {
                var exeFiles = Directory.GetFiles(toolDir, "*.exe")
                    .Where(f => !f.Contains("x86", StringComparison.OrdinalIgnoreCase) && !f.Contains("x32", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exeFiles.Count == 0)
                    exeFiles = Directory.GetFiles(toolDir, "*.exe").ToList();
                if (exeFiles.Count == 0)
                    exeFiles = Directory.GetFiles(toolDir, "*.bat").ToList();
                foreach (var f in exeFiles)
                    existingFiles.Add(Path.GetRelativePath(baseDir, f));
            }
        }

        bool changed = false;

        // 删除已不存在的工具
        for (int i = _allTools.Count - 1; i >= 0; i--)
        {
            var action = _allTools[i].Action ?? "";
            if (action.StartsWith("tools\\", StringComparison.OrdinalIgnoreCase) && !existingFiles.Contains(action))
            {
                _allTools.RemoveAt(i);
                changed = true;
            }
        }

        // 添加新增的工具
        var existingActions = new HashSet<string>(_allTools.Select(t => t.Action ?? ""), StringComparer.OrdinalIgnoreCase);

        foreach (var catDir in Directory.GetDirectories(toolsDir))
        {
            var category = MapCategory(Path.GetFileName(catDir));
            foreach (var toolDir in Directory.GetDirectories(catDir))
            {
                var toolName = Path.GetFileName(toolDir);
                var exeFiles = Directory.GetFiles(toolDir, "*.exe")
                    .Where(f => !f.Contains("x86", StringComparison.OrdinalIgnoreCase) && !f.Contains("x32", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exeFiles.Count == 0)
                    exeFiles = Directory.GetFiles(toolDir, "*.exe").ToList();
                if (exeFiles.Count == 0)
                    exeFiles = Directory.GetFiles(toolDir, "*.bat").ToList();
                if (exeFiles.Count == 0) continue;

                var exePath = exeFiles[0];
                var relPath = Path.GetRelativePath(baseDir, exePath);

                if (existingActions.Contains(relPath)) continue;

                _allTools.Add(new ToolInfo
                {
                    Icon = relPath,
                    Name = toolName,
                    Description = $"{category}工具",
                    Action = relPath,
                    Category = category
                });
                changed = true;
            }
        }

        if (changed) SaveTools();
    }

    private void SaveTools()
    {
        var json = JsonSerializer.Serialize(_allTools);
        try { File.WriteAllText(ToolsFile, json); } catch { }
        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools.json"), json); } catch { }
    }

    private void InitDefaultTools()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sys32 = Path.Combine(winDir, "System32");
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var toolsDir = Path.Combine(baseDir, "tools");

        var tools = new List<ToolInfo>();

        // 扫描 tools 目录按分类添加工具
        if (Directory.Exists(toolsDir))
        {
            foreach (var catDir in Directory.GetDirectories(toolsDir))
            {
                var category = Path.GetFileName(catDir);
                foreach (var toolDir in Directory.GetDirectories(catDir))
                {
                    var toolName = Path.GetFileName(toolDir);
                    var exeFiles = Directory.GetFiles(toolDir, "*.exe")
                        .Where(f => !f.Contains("x86", StringComparison.OrdinalIgnoreCase) && !f.Contains("x32", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (exeFiles.Count == 0)
                        exeFiles = Directory.GetFiles(toolDir, "*.exe").ToList();
                    if (exeFiles.Count == 0)
                        exeFiles = Directory.GetFiles(toolDir, "*.bat").ToList();

                    if (exeFiles.Count > 0)
                    {
                        var exePath = exeFiles[0];
                        var relPath = Path.GetRelativePath(baseDir, exePath);
                        tools.Add(new ToolInfo
                        {
                            Icon = relPath,
                            Name = toolName,
                            Description = $"{category}工具",
                            Action = relPath,
                            Category = MapCategory(category)
                        });
                    }
                }
            }
        }

        // 系统内置工具
        tools.AddRange([
            new ToolInfo { Icon=$"{sys32}\\diskmgmt.msc",  Name="磁盘管理",    Description="Windows 磁盘管理",     Action="diskmgmt.msc",              Category="硬盘工具" },
            new ToolInfo { Icon=$"{sys32}\\devmgmt.msc",   Name="设备管理器",  Description="管理硬件设备驱动",     Action="devmgmt.msc",               Category="外设工具" },
            new ToolInfo { Icon=$"{sys32}\\Taskmgr.exe",   Name="任务管理器",  Description="查看系统进程与性能",   Action="taskmgr.exe",               Category="CPU工具" },
            new ToolInfo { Icon=$"{sys32}\\dxdiag.exe",    Name="DirectX诊断",Description="DirectX 诊断工具",     Action="dxdiag.exe",                Category="显卡工具" },
            new ToolInfo { Icon=$"{sys32}\\msinfo32.exe",  Name="系统信息",    Description="查看详细系统信息",     Action="msinfo32.exe",              Category="CPU工具" },
            new ToolInfo { Icon=$"{sys32}\\regedit.exe",   Name="注册表编辑器",Description="Windows 注册表编辑",   Action="regedit.exe",               Category="其他工具" },
            new ToolInfo { Icon=$"{sys32}\\cmd.exe",       Name="Ping测试",    Description="测试网络连通性",       Action="cmd:ping www.baidu.com",    Category="网络工具" },
            new ToolInfo { Icon=$"{sys32}\\cmd.exe",       Name="IP配置",      Description="查看网络配置信息",     Action="cmd:ipconfig /all",         Category="网络工具" },
            new ToolInfo { Icon=$"{sys32}\\cmd.exe",       Name="网络连接",    Description="查看当前网络连接",     Action="cmd:netstat -an",           Category="网络工具" },
            new ToolInfo { Icon=$"{sys32}\\notepad.exe",   Name="记事本",      Description="Windows 文本编辑器",   Action="notepad.exe",               Category="其他工具" },
            new ToolInfo { Icon=$"{sys32}\\calc.exe",      Name="计算器",      Description="Windows 计算器",       Action="calc.exe",                  Category="其他工具" },
            new ToolInfo { Icon=$"{sys32}\\mspaint.exe",   Name="画图工具",    Description="Windows 画图工具",     Action="mspaint.exe",               Category="其他工具" },
            new ToolInfo { Icon=$"{sys32}\\cmd.exe",       Name="命令提示符",  Description="CMD 命令行工具",       Action="cmd.exe",                   Category="其他工具" },
            new ToolInfo { Icon=$"{sys32}\\control.exe",   Name="控制面板",    Description="Windows 控制面板",     Action="control.exe",               Category="其他工具" },
            new ToolInfo { Icon="icon\\store.png",         Name="驱动管理",    Description="驱动安装与更新",       Action="msg:即将推出",              Category="工具商店" },
            new ToolInfo { Icon="icon\\store.png",         Name="系统备份",    Description="系统备份与还原",       Action="msg:即将推出",              Category="工具商店" },
            new ToolInfo { Icon="icon\\store.png",         Name="数据恢复",    Description="文件数据恢复工具",     Action="msg:即将推出",              Category="工具商店" },
            new ToolInfo { Icon="icon\\store.png",         Name="远程桌面",    Description="远程连接工具",         Action="msg:即将推出",              Category="工具商店" },
        ]);

        _allTools = tools;
        SaveTools();
    }

    private static string MapCategory(string folderName)
    {
        return folderName switch
        {
            "处理器工具" => "CPU工具",
            "内存工具" => "内存工具",
            "显卡工具" => "显卡工具",
            "硬盘工具" => "硬盘工具",
            "外设工具" => "外设工具",
            "烤鸡工具" => "烤鸡工具",
            "综合检测" => "CPU工具",
            "其他工具" => "其他工具",
            "系统工具" => "其他工具",
            "显示工具" => "外设工具",
            "游戏工具" => "其他工具",
            _ => folderName
        };
    }

    private void ToggleFavorite(ToolInfo tool)
    {
        if (_favorites.Contains(tool.Name))
            _favorites.Remove(tool.Name);
        else
            _favorites.Add(tool.Name);
        SaveFavorites();
    }

    private bool IsFavorite(ToolInfo tool) => _favorites.Contains(tool.Name);

    #endregion

    #region 导航

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString() ?? "system";
            LoadContent(tag);
        }
    }

    private string GetCurrentTag()
    {
        if (navView.SelectedItem is NavigationViewItem item)
            return item.Tag?.ToString() ?? "system";
        return "system";
    }

    private void LoadContent(string tag)
    {
        _currentTag = tag;
        _chartTimer?.Stop();
        contentArea.Children.Clear();

        // 显示/隐藏返回按钮
        backBtn.Visibility = tag == "system" ? Visibility.Collapsed : Visibility.Visible;

        switch (tag)
        {
            case "system":
                navView.Header = "系统信息";
                ShowSystemInfo();
                break;
            case "favorites":
                navView.Header = "收藏夹";
                ShowFavorites();
                break;
            case "network":
                navView.Header = "网络工具";
                ShowNetworkTools();
                break;
            default:
                navView.Header = tag;
                ShowCategory(tag);
                break;
        }
    }

    #endregion

    #region 页面展示

    private void ShowFavorites()
    {
        var favs = _allTools.Where(t => IsFavorite(t)).ToList();
        if (favs.Count == 0)
        {
            contentArea.Children.Add(new TextBlock
            {
                Text = "暂无收藏，点击工具卡片右上角 ☆ 添加",
                FontSize = 16,
                Foreground = _textSecondaryBrush,
                Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }
        contentArea.Children.Add(CreateToolGrid(favs));
    }

    private void ShowCategory(string category)
    {
        var tools = _allTools.Where(t => t.Category == category).ToList();
        if (tools.Count == 0)
        {
            contentArea.Children.Add(new TextBlock
            {
                Text = "暂无工具",
                FontSize = 16,
                Foreground = _textSecondaryBrush,
                Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }
        contentArea.Children.Add(CreateToolGrid(tools));
    }

    private void ShowNetworkTools()
    {
        var stackPanel = new StackPanel();

        // 选项卡
        var tabBar = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        tabBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tabBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tabBar.ColumnDefinitions.Add(new ColumnDefinition());

        var ipTabBtn = new Button
        {
            Content = "IP 查询",
            FontSize = 15,
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
        };
        var splitTabBtn = new Button
        {
            Content = "分流测试",
            FontSize = 15,
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0)),
            Foreground = _textSecondaryBrush,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(ipTabBtn, 0);
        Grid.SetColumn(splitTabBtn, 1);
        tabBar.Children.Add(ipTabBtn);
        tabBar.Children.Add(splitTabBtn);
        stackPanel.Children.Add(tabBar);

        var ipPanel = new StackPanel();
        BuildIPTab(ipPanel);
        stackPanel.Children.Add(ipPanel);

        var splitPanel = new StackPanel { Visibility = Visibility.Collapsed };
        BuildSplitTab(splitPanel);
        stackPanel.Children.Add(splitPanel);

        contentArea.Children.Add(stackPanel);

        ipTabBtn.Click += (s, e) => SwitchTab(ipTabBtn, splitTabBtn, ipPanel, splitPanel);
        splitTabBtn.Click += (s, e) => SwitchTab(splitTabBtn, ipTabBtn, splitPanel, ipPanel);
    }

    private void SwitchTab(Button active, Button inactive, Panel activePanel, Panel inactivePanel)
    {
        active.Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212));
        active.Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255));
        inactive.Background = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0));
        inactive.Foreground = _textSecondaryBrush;
        activePanel.Visibility = Visibility.Visible;
        inactivePanel.Visibility = Visibility.Collapsed;
    }

    private void BuildIPTab(StackPanel parent)
    {
        var ipCard = new Border { Background = _cardBgBrush, BorderBrush = _cardBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 12) };
        var ipGrid = new Grid { Margin = new Thickness(20) };
        ipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ip4Panel = new StackPanel();
        ip4Panel.Children.Add(new TextBlock { Text = "IPv4", FontSize = 13, Foreground = _textSecondaryBrush });
        var ip4Val = new TextBlock { Text = "查询中...", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = _textPrimaryBrush, Margin = new Thickness(0, 4, 0, 0) };
        ip4Panel.Children.Add(ip4Val);
        var ip4LocVal = new TextBlock { Text = "", FontSize = 13, Foreground = _textSecondaryBrush, Margin = new Thickness(0, 2, 0, 0) };
        ip4Panel.Children.Add(ip4LocVal);
        var ip4IspVal = new TextBlock { Text = "", FontSize = 13, Foreground = _textSecondaryBrush };
        ip4Panel.Children.Add(ip4IspVal);
        ipGrid.Children.Add(ip4Panel);

        var sep = new Border { Width = 1, Background = _cardBorderBrush, Margin = new Thickness(24, 0, 24, 0) };
        Grid.SetColumn(sep, 1);
        ipGrid.Children.Add(sep);

        var ip6Panel = new StackPanel();
        ip6Panel.Children.Add(new TextBlock { Text = "IPv6", FontSize = 13, Foreground = _textSecondaryBrush });
        var ip6Val = new TextBlock { Text = "查询中...", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = _textPrimaryBrush, Margin = new Thickness(0, 4, 0, 0) };
        ip6Panel.Children.Add(ip6Val);
        var ip6LocVal = new TextBlock { Text = "", FontSize = 13, Foreground = _textSecondaryBrush, Margin = new Thickness(0, 2, 0, 0) };
        ip6Panel.Children.Add(ip6LocVal);
        var ip6IspVal = new TextBlock { Text = "", FontSize = 13, Foreground = _textSecondaryBrush };
        ip6Panel.Children.Add(ip6IspVal);
        Grid.SetColumn(ip6Panel, 2);
        ipGrid.Children.Add(ip6Panel);

        ipCard.Child = ipGrid;
        parent.Children.Add(ipCard);

        // 多源 IP 查询
        var sourceCard = new Border { Background = _cardBgBrush, BorderBrush = _cardBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 12) };
        var sourceStack = new StackPanel();
        sourceStack.Children.Add(new TextBlock { Text = "多源 IP 查询", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        var sources = new (string name, string tag, string url)[] {
            ("iP138.com", "国内", "https://api.yaohud.cn/api/v5/ip138"),
            ("IP.cn", "国内", "https://ip9.com.cn/get"),
            ("Cloudflare", "国际", "https://api.ipify.org?format=json"),
            ("IPinfo.io", "国际", "https://ipinfo.io/json")
        };
        var sourceLabels = new TextBlock[sources.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            namePanel.Children.Add(new TextBlock { Text = sources[i].name, FontSize = 14, Foreground = _textPrimaryBrush, VerticalAlignment = VerticalAlignment.Center });
            var tag = new Border
            {
                Background = sources[i].tag == "国内" ? new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0)) : new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock { Text = sources[i].tag, FontSize = 11, Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)) }
            };
            namePanel.Children.Add(tag);
            row.Children.Add(namePanel);
            sourceLabels[i] = new TextBlock { Text = "查询中...", FontSize = 14, Foreground = _textPrimaryBrush };
            Grid.SetColumn(sourceLabels[i], 2);
            row.Children.Add(sourceLabels[i]);
            sourceStack.Children.Add(row);
        }
        sourceCard.Child = sourceStack;
        parent.Children.Add(sourceCard);

        // 连通性测试
        var pingCard = new Border { Background = _cardBgBrush, BorderBrush = _cardBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 12) };
        var pingStack = new StackPanel();
        var pingHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        pingHeader.ColumnDefinitions.Add(new ColumnDefinition());
        pingHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pingHeader.Children.Add(new TextBlock { Text = "网络连通性测试", FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var pingRefreshBtn = new Button
        {
            Content = "🚀",
            FontSize = 18,
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(pingRefreshBtn, 1);
        pingHeader.Children.Add(pingRefreshBtn);
        pingStack.Children.Add(pingHeader);

        pingStack.Children.Add(new TextBlock { Text = "国内", FontSize = 14, Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var domesticTargets = new[] {
            ("Bilibili", "bilibili.com", "bilibili.png"),
            ("微信", "weixin.qq.com", "wechat.png"),
            ("淘宝", "taobao.com", (string)null),
            ("字节跳动", "bytedance.com", "douyin.png")
        };
        var pingGrid1 = new Grid();
        pingGrid1.ColumnDefinitions.Add(new ColumnDefinition());
        pingGrid1.ColumnDefinitions.Add(new ColumnDefinition());
        pingGrid1.ColumnDefinitions.Add(new ColumnDefinition());
        var domesticLabels = AddPingRows(pingGrid1, domesticTargets);
        pingStack.Children.Add(pingGrid1);

        pingStack.Children.Add(new TextBlock { Text = "国际", FontSize = 14, Foreground = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var intlTargets = new[] {
            ("GitHub", "github.com", (string)null),
            ("jsDelivr", "cdn.jsdelivr.net", (string)null),
            ("Cloudflare", "cloudflare.com", (string)null),
            ("YouTube", "youtube.com", (string)null)
        };
        var pingGrid2 = new Grid();
        pingGrid2.ColumnDefinitions.Add(new ColumnDefinition());
        pingGrid2.ColumnDefinitions.Add(new ColumnDefinition());
        pingGrid2.ColumnDefinitions.Add(new ColumnDefinition());
        var intlLabels = AddPingRows(pingGrid2, intlTargets);
        pingStack.Children.Add(pingGrid2);

        pingCard.Child = pingStack;
        parent.Children.Add(pingCard);

        _ = QueryIPInfo(ip4Val, ip4LocVal, ip4IspVal, null, ip6Val, ip6LocVal, ip6IspVal);
        for (int i = 0; i < sources.Length; i++)
        {
            var idx = i;
            _ = QuerySourceIP(sources[idx].url, sourceLabels[idx]);
        }
        _ = RunPingTests(domesticTargets, domesticLabels);
        _ = RunPingTests(intlTargets, intlLabels);
        pingRefreshBtn.Click += async (s, e) =>
        {
            pingRefreshBtn.IsEnabled = false;
            await RunPingTests(domesticTargets, domesticLabels);
            await RunPingTests(intlTargets, intlLabels);
            pingRefreshBtn.IsEnabled = true;
        };
    }

    private void BuildSplitTab(StackPanel parent)
    {
        var refreshBtn = new Button
        {
            Content = "🚀 测试全部",
            FontSize = 14,
            Padding = new Thickness(16, 6, 16, 6),
            Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        parent.Children.Add(refreshBtn);

        var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var tablePanel = new StackPanel();
        scrollViewer.Content = tablePanel;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        header.Children.Add(new TextBlock { Text = "网站", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = _textSecondaryBrush });
        var ipHeader = new TextBlock { Text = "IP 地址", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = _textSecondaryBrush };
        Grid.SetColumn(ipHeader, 1);
        header.Children.Add(ipHeader);
        var locHeader = new TextBlock { Text = "归属地", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = _textSecondaryBrush };
        Grid.SetColumn(locHeader, 2);
        header.Children.Add(locHeader);
        tablePanel.Children.Add(header);

        var sites = new (string name, string domain, string tag, string icon)[]
        {
            ("哔哩哔哩", "bilibili.com", "国内", "bilibili.png"),
            ("阿里巴巴", "alibaba.com", "国内", "ali.png"),
            ("网易", "163.com", "国内", null),
            ("字节跳动", "bytedance.com", "国内", "douyin.png"),
            ("腾讯", "qq.com", "国内", null),
            ("百度", "baidu.com", "国内", null),
            ("Cloudflare", "cloudflare.com", "国际", null),
            ("TikTok", "tiktok.com", "国际", null),
            ("Discord", "discord.com", "国际", null),
            ("X (Twitter)", "x.com", "国际", null),
            ("Medium", "medium.com", "国际", null),
            ("ChatGPT", "chatgpt.com", "AI", null),
            ("OpenAI", "openai.com", "AI", null),
            ("Claude", "claude.ai", "AI", null),
            ("Perplexity", "perplexity.ai", "AI", null),
            ("Coinbase", "coinbase.com", "Crypto", null),
            ("OKX", "okx.com", "Crypto", null),
        };

        var siteLabels = new (TextBlock ip, TextBlock loc)[sites.Length];
        string currentGroup = "";
        for (int i = 0; i < sites.Length; i++)
        {
            if (sites[i].tag != currentGroup)
            {
                currentGroup = sites[i].tag;
                var groupTag = new Border
                {
                    Background = currentGroup == "国内" ? new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0))
                        : currentGroup == "国际" ? new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212))
                        : currentGroup == "AI" ? new SolidColorBrush(WColor.FromArgb(255, 0, 180, 100))
                        : new SolidColorBrush(WColor.FromArgb(255, 150, 80, 200)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 12, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = currentGroup, FontSize = 14, Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)), FontWeight = FontWeights.Bold }
                };
                tablePanel.Children.Add(groupTag);
            }

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var iconFile = sites[i].icon ?? sites[i].domain.Split('.')[0] + ".png";
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", iconFile);
            if (File.Exists(iconPath))
            {
                try
                {
                    var img = new Image
                    {
                        Source = new BitmapImage(new Uri(iconPath)),
                        Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0)
                    };
                    namePanel.Children.Add(img);
                }
                catch { }
            }
            namePanel.Children.Add(new TextBlock { Text = sites[i].name, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = _textPrimaryBrush, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(namePanel);
            var ipTb = new TextBlock { Text = "--", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = _textSecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(ipTb, 1);
            row.Children.Add(ipTb);
            var locTb = new TextBlock { Text = "", FontSize = 16, Foreground = _textSecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(locTb, 2);
            row.Children.Add(locTb);
            tablePanel.Children.Add(row);
            siteLabels[i] = (ipTb, locTb);
        }

        parent.Children.Add(scrollViewer);

        refreshBtn.Click += async (s, e) =>
        {
            refreshBtn.IsEnabled = false;
            refreshBtn.Content = "🚀 测试中...";
            for (int i = 0; i < sites.Length; i++)
            {
                siteLabels[i].ip.Text = "解析中...";
                siteLabels[i].loc.Text = "";
                try
                {
                    var addr = Dns.GetHostAddresses(sites[i].domain);
                    var ipv4 = addr.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 != null)
                    {
                        siteLabels[i].ip.Text = ipv4.ToString();
                        _ = QueryGeo(ipv4.ToString(), siteLabels[i].loc);
                    }
                    else
                    {
                        var ipv6 = addr.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
                        siteLabels[i].ip.Text = ipv6?.ToString() ?? "无结果";
                    }
                }
                catch { siteLabels[i].ip.Text = "解析失败"; }
            }
            refreshBtn.Content = "🚀 测试全部";
            refreshBtn.IsEnabled = true;
        };
    }

    private async Task QueryGeo(string ip, TextBlock label)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await client.GetStringAsync($"http://ip-api.com/json/{ip}?fields=country,regionName,city");
            var doc = JsonDocument.Parse(json);
            var country = doc.RootElement.GetProperty("country").GetString() ?? "";
            var region = doc.RootElement.GetProperty("regionName").GetString() ?? "";
            var city = doc.RootElement.GetProperty("city").GetString() ?? "";
            DispatcherQueue.TryEnqueue(() => label.Text = $"{country} {region} {city}".Trim());
        }
        catch { DispatcherQueue.TryEnqueue(() => label.Text = "未知"); }
    }

    private TextBlock[] AddPingRows(Grid grid, (string, string, string)[] targets)
    {
        var labels = new TextBlock[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            if (i % 3 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var rowDef = new Grid { Margin = new Thickness(0, 4, 12, 4) };
            rowDef.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            rowDef.ColumnDefinitions.Add(new ColumnDefinition());
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var iconFile = targets[i].Item3 ?? targets[i].Item2.Split('.')[0] + ".png";
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", iconFile);
            if (File.Exists(iconPath))
            {
                try
                {
                    var img = new Image
                    {
                        Source = new BitmapImage(new Uri(iconPath)),
                        Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0)
                    };
                    namePanel.Children.Add(img);
                }
                catch { }
            }
            namePanel.Children.Add(new TextBlock { Text = targets[i].Item1, FontSize = 13 });
            rowDef.Children.Add(namePanel);
            labels[i] = new TextBlock { Text = "-- ms", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = _textSecondaryBrush };
            Grid.SetColumn(labels[i], 1);
            rowDef.Children.Add(labels[i]);
            Grid.SetColumn(rowDef, i % 3);
            Grid.SetRow(rowDef, i / 3);
            grid.Children.Add(rowDef);
        }
        return labels;
    }

    private async Task RunPingTests((string, string, string)[] targets, TextBlock[] labels)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            labels[i].Text = "ping...";
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(targets[i].Item2, 3000);
                labels[i].Text = reply.Status == IPStatus.Success
                    ? $"{reply.RoundtripTime} ms"
                    : "超时";
            }
            catch { labels[i].Text = "失败"; }
        }
    }

    private async Task QuerySourceIP(string url, TextBlock label)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var resp = await client.GetStringAsync(url);
            string ip;
            if (url.Contains("cdn-cgi/trace"))
            {
                var match = Regex.Match(resp, @"ip=(\S+)");
                ip = match.Success ? match.Groups[1].Value : "解析失败";
            }
            else if (resp.TrimStart().StartsWith("{"))
            {
                var doc = JsonDocument.Parse(resp);
                var root = doc.RootElement;
                if (root.TryGetProperty("ip", out var ipProp))
                    ip = ipProp.GetString() ?? "解析失败";
                else if (root.TryGetProperty("data", out var data) && data.TryGetProperty("ip", out var dataIp))
                    ip = dataIp.GetString() ?? "解析失败";
                else
                    ip = "解析失败";
            }
            else
            {
                ip = resp.Trim();
            }
            DispatcherQueue.TryEnqueue(() => label.Text = ip);
        }
        catch { DispatcherQueue.TryEnqueue(() => label.Text = "查询失败"); }
    }

    private async Task QueryIPInfo(TextBlock ip4Val, TextBlock ip4Loc, TextBlock ip4Isp, TextBlock ip4Asn, TextBlock ip6Val, TextBlock ip6Loc, TextBlock ip6Isp)
    {
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            var json = await client.GetStringAsync("https://ip9.com.cn/get");
            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            var ip = data.GetProperty("ip").GetString() ?? "";
            var country = data.GetProperty("country").GetString() ?? "";
            var prov = data.GetProperty("prov").GetString() ?? "";
            var city = data.GetProperty("city").GetString() ?? "";
            var isp = data.GetProperty("isp").GetString() ?? "";
            DispatcherQueue.TryEnqueue(() =>
            {
                ip4Val.Text = ip;
                ip4Loc.Text = $"{country} {prov} {city}".Trim();
                ip4Isp.Text = isp;
            });
        }
        catch
        {
            try
            {
                var json = await client.GetStringAsync("http://ip-api.com/json/?fields=query,country,regionName,city,isp,org,as");
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                DispatcherQueue.TryEnqueue(() =>
                {
                    ip4Val.Text = root.GetProperty("query").GetString() ?? "";
                    ip4Loc.Text = $"{root.GetProperty("country").GetString()} {root.GetProperty("regionName").GetString()} {root.GetProperty("city").GetString()}".Trim();
                    ip4Isp.Text = root.GetProperty("isp").GetString() ?? "";
                    if (ip4Asn != null) ip4Asn.Text = root.GetProperty("as").GetString() ?? "";
                });
            }
            catch { DispatcherQueue.TryEnqueue(() => ip4Val.Text = "查询失败"); }
        }

        try
        {
            var ipv6 = await client.GetStringAsync("https://api6.ipify.org");
            var ipv6Str = ipv6.Trim();
            DispatcherQueue.TryEnqueue(() => ip6Val.Text = ipv6Str);
            try
            {
                var v6json = await client.GetStringAsync($"http://ip-api.com/json/{ipv6Str}?fields=query,country,regionName,city,isp");
                var v6doc = JsonDocument.Parse(v6json);
                var v6root = v6doc.RootElement;
                DispatcherQueue.TryEnqueue(() =>
                {
                    ip6Loc.Text = $"{v6root.GetProperty("country").GetString()} {v6root.GetProperty("regionName").GetString()} {v6root.GetProperty("city").GetString()}".Trim();
                    ip6Isp.Text = v6root.GetProperty("isp").GetString() ?? "";
                });
            }
            catch { }
        }
        catch { DispatcherQueue.TryEnqueue(() => ip6Val.Text = "无 IPv6"); }
    }

    #endregion

    #region 工具卡片

    private Grid CreateToolGrid(List<ToolInfo> tools)
    {
        var grid = new Grid();
        int cols = 3;
        for (int i = 0; i < cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());

        int row = 0;
        for (int i = 0; i < tools.Count; i++)
        {
            if (i % cols == 0)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var card = CreateToolCard(tools[i]);
            Grid.SetColumn(card, i % cols);
            Grid.SetRow(card, row);
            grid.Children.Add(card);

            if ((i + 1) % cols == 0) row++;
        }

        int remainder = tools.Count % cols;
        if (remainder != 0)
        {
            for (int j = remainder; j < cols; j++)
            {
                var spacer = new Border { Opacity = 0 };
                Grid.SetColumn(spacer, j);
                Grid.SetRow(spacer, row);
                grid.Children.Add(spacer);
            }
        }

        return grid;
    }

    private Border CreateToolCard(ToolInfo tool)
    {
        bool isFav = IsFavorite(tool);

        var card = new Border
        {
            Background = _cardBgBrush,
            BorderBrush = _cardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(6),
            Padding = new Thickness(16),
            MinHeight = 120
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement iconElement;
        if (!string.IsNullOrEmpty(tool.Icon) && (tool.Icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                                   tool.Icon.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var iconPath = tool.Icon;
            if (!Path.IsPathRooted(iconPath))
                iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconPath);
            iconElement = LoadExeIcon(iconPath, 36);
        }
        else if (!string.IsNullOrEmpty(tool.Icon) && (tool.Icon.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
        {
            var iconPath = tool.Icon;
            if (!Path.IsPathRooted(iconPath))
                iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconPath);
            iconElement = LoadSvgIcon(iconPath, 36);
        }
        else if (!string.IsNullOrEmpty(tool.Icon) && (tool.Icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                                        tool.Icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                                                        tool.Icon.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                        tool.Icon.Contains(@"\") || tool.Icon.Contains("/")))
        {
            var iconPath = tool.Icon;
            if (!Path.IsPathRooted(iconPath))
                iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconPath);
            var img = new Image { Width = 36, Height = 36, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            try
            {
                if (File.Exists(iconPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bitmap.DecodePixelWidth = 72;
                    img.Source = bitmap;
                }
            }
            catch { }
            iconElement = img;
        }
        else
        {
            iconElement = new TextBlock
            {
                Text = tool.Icon ?? "\uE71C",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 32,
                Foreground = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
        }
        if (iconElement == null)
        {
            iconElement = new TextBlock
            {
                Text = "\uE71C",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 32,
                Foreground = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
        }
        Grid.SetColumn((FrameworkElement)iconElement, 0);

        var nameBlock = new TextBlock
        {
            Text = tool.Name,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameBlock, 1);

        var favBtn = new Button
        {
            Content = isFav ? "\uE735" : "\uE734",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Background = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Foreground = isFav ? new SolidColorBrush(WColor.FromArgb(255, 255, 180, 0)) : _textSecondaryBrush,
            Padding = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Top,
            Tag = tool
        };
        favBtn.Click += FavBtn_Click;
        Grid.SetColumn(favBtn, 2);

        headerRow.Children.Add(iconElement);
        headerRow.Children.Add(nameBlock);
        headerRow.Children.Add(favBtn);
        Grid.SetRow(headerRow, 0);

        var descBlock = new TextBlock
        {
            Text = tool.Description,
            FontSize = 13,
            Foreground = _textSecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(48, 6, 0, 0)
        };
        Grid.SetRow(descBlock, 1);

        var bottomRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition());

        var settingsBtn = new Border
        {
            Width = 30,
            Height = 30,
            Background = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = tool
        };
        settingsBtn.Child = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = "\uE713",
            FontSize = 15,
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 150, 150, 150))
        };
        settingsBtn.Tapped += EditBtn_Tapped;
        Grid.SetColumn(settingsBtn, 0);

        var openBtn = new Button
        {
            Content = "打开",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6),
            Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Tag = tool
        };
        openBtn.Click += OpenBtn_Click;
        Grid.SetColumn(openBtn, 1);

        bottomRow.Children.Add(settingsBtn);
        bottomRow.Children.Add(openBtn);
        Grid.SetRow(bottomRow, 3);

        root.Children.Add(headerRow);
        root.Children.Add(descBlock);
        root.Children.Add(bottomRow);

        card.Child = root;
        return card;
    }

    private void FavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolInfo tool)
        {
            ToggleFavorite(tool);
            LoadContent(GetCurrentTag());
        }
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolInfo tool)
            ExecuteToolAction(tool.Action);
    }

    private void EditBtn_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ToolInfo tool)
        {
            int idx = _allTools.IndexOf(tool);
            if (idx >= 0)
                ShowEditDialog(tool, idx);
        }
    }

    private async void ShowEditDialog(ToolInfo tool, int idx)
    {
        var nameBox = new TextBox { Text = tool.Name, Margin = new Thickness(0, 0, 0, 8) };
        var descBox = new TextBox { Text = tool.Description, Margin = new Thickness(0, 0, 0, 8) };
        var actionBox = new TextBox { Text = tool.Action, Margin = new Thickness(0, 0, 0, 8) };
        var iconBox = new TextBox { Text = tool.Icon ?? "", Margin = new Thickness(0, 0, 0, 8) };
        var catBox = new TextBox { Text = tool.Category ?? "", Margin = new Thickness(0, 0, 0, 8) };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "名称", Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "描述", Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(descBox);
        panel.Children.Add(new TextBlock { Text = "路径/命令", Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(actionBox);
        panel.Children.Add(new TextBlock { Text = "图标", Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(iconBox);
        panel.Children.Add(new TextBlock { Text = "分类", Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(catBox);

        var dlg = new ContentDialog
        {
            Title = "编辑工具 - " + tool.Name,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = panel,
            XamlRoot = contentArea.XamlRoot
        };

        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            tool.Name = nameBox.Text;
            tool.Description = descBox.Text;
            tool.Action = actionBox.Text;
            tool.Icon = string.IsNullOrWhiteSpace(iconBox.Text) ? null : iconBox.Text;
            tool.Category = string.IsNullOrWhiteSpace(catBox.Text) ? null : catBox.Text;
            SaveTools();
            LoadContent(GetCurrentTag());
        }
    }

    private void ExecuteToolAction(string action)
    {
        if (string.IsNullOrEmpty(action)) return;
        try
        {
            if (action.StartsWith("msg:"))
                ShowMessageDialog(action.Substring(4));
            else if (action.StartsWith("cmd:"))
                Process.Start(new ProcessStartInfo("cmd.exe", "/c " + action.Substring(4) + " & pause") { UseShellExecute = true });
            else
            {
                var path = action;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            ShowMessageDialog("启动失败: " + ex.Message);
        }
    }

    private async void ShowMessageDialog(string message, string title = "提示")
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = contentArea.XamlRoot
        };
        await dlg.ShowAsync();
    }

    private UIElement LoadExeIcon(string path, double size)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                using var stream = new MemoryStream();
                icon.ToBitmap().Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Seek(0, SeekOrigin.Begin);
                var bitmap = new BitmapImage();
                bitmap.SetSource(stream.AsRandomAccessStream());
                return new Image
                {
                    Source = bitmap,
                    Width = size, Height = size,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
            }
        }
        catch { }
        return null;
    }

    private UIElement LoadSvgIcon(string path, double size)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var svgContent = File.ReadAllText(path);
            var doc = new System.Xml.XmlDocument { XmlResolver = null };
            doc.LoadXml(svgContent);

            var svgNode = doc.SelectSingleNode("svg") ?? doc.DocumentElement;
            var viewBox = svgNode?.Attributes?["viewBox"]?.Value ?? "0 0 24 24";

            var fillAttr = svgNode?.Attributes?["fill"]?.Value;
            var fillColor = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212));
            if (!string.IsNullOrEmpty(fillAttr) && fillAttr != "none")
            {
                try { fillColor = new SolidColorBrush(ParseColor(fillAttr)); } catch { }
            }

            var geometryGroup = new GeometryGroup();
            var pathNodes = svgNode?.SelectNodes(".//path");
            if (pathNodes != null)
            {
                foreach (System.Xml.XmlNode pathNode in pathNodes)
                {
                    var d = pathNode.Attributes?["d"]?.Value;
                    if (!string.IsNullOrEmpty(d))
                    {
                        try
                        {
                            // 在 WinUI 3 中，需要使用 XamlReader 解析路径数据
                            var geo = ParseSvgPathData(d);
                            if (geo != null)
                                geometryGroup.Children.Add(geo);
                        }
                        catch { }
                    }
                }
            }

            var circleNodes = svgNode?.SelectNodes(".//circle");
            if (circleNodes != null)
            {
                foreach (System.Xml.XmlNode cn in circleNodes)
                {
                    var cx = double.Parse(cn.Attributes?["cx"]?.Value ?? "0");
                    var cy = double.Parse(cn.Attributes?["cy"]?.Value ?? "0");
                    var r = double.Parse(cn.Attributes?["r"]?.Value ?? "0");
                    geometryGroup.Children.Add(new EllipseGeometry { Center = new Point(cx, cy), RadiusX = r, RadiusY = r });
                }
            }

            var rectNodes = svgNode?.SelectNodes(".//rect");
            if (rectNodes != null)
            {
                foreach (System.Xml.XmlNode rn in rectNodes)
                {
                    var x = double.Parse(rn.Attributes?["x"]?.Value ?? "0");
                    var y = double.Parse(rn.Attributes?["y"]?.Value ?? "0");
                    var w = double.Parse(rn.Attributes?["width"]?.Value ?? "0");
                    var h = double.Parse(rn.Attributes?["height"]?.Value ?? "0");
                    geometryGroup.Children.Add(new RectangleGeometry { Rect = new Rect(x, y, w, h) });
                }
            }

            if (geometryGroup.Children.Count == 0) return null;

            var pathElement = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometryGroup,
                Fill = fillColor,
                Stretch = Stretch.Uniform,
                Width = size, Height = size,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            pathElement.Measure(new Size(size, size));
            return pathElement;
        }
        catch { }
        return null;
    }

    private Geometry ParseSvgPathData(string d)
    {
        // 使用 XamlReader 解析路径数据
        var xaml = $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{d}'/>";
        var path = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        return path.Data;
    }

    private WColor ParseColor(string hex)
    {
        if (hex.StartsWith("#"))
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return WColor.FromArgb(255, r, g, b);
            }
            else if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return WColor.FromArgb(a, r, g, b);
            }
        }
        return WColor.FromArgb(255, 0, 120, 212);
    }

    #endregion

    #region 切换侧边栏

    private void PaneToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        navView.IsPaneOpen = !navView.IsPaneOpen;
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        // 返回到系统信息页面
        navView.SelectedItem = navSystem;
        LoadContent("system");
        backBtn.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region 设置对话框

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        navView.SelectedItem = null;
        navView.Header = "设置";
        contentArea.Children.Clear();
        contentArea.Children.Add(new Pages.SettingsPage(this));
        backBtn.Visibility = Visibility.Visible;
    }

    #endregion

    #region 系统信息

    private void ShowSystemInfo()
    {
        if (_systemInfoCache != null) { contentArea.Children.Add(_systemInfoCache); return; }

        var stackPanel = new StackPanel();
        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition());
        topRow.ColumnDefinitions.Add(new ColumnDefinition());
        topRow.ColumnDefinitions.Add(new ColumnDefinition());

        var cpuCard = CreateInfoCard("CPU 信息", new[] { CreateInfoRow("处理器", _cpuInfo), CreateInfoRow("核心数", _cpuCores), CreateInfoRow("频率", _cpuFreq) });
        cpuCard.Margin = new Thickness(0, 0, 4, 8);
        Grid.SetColumn(cpuCard, 0);

        var memCard = CreateInfoCard("内存信息", new[] { CreateInfoRow("总内存", _totalMemory), CreateInfoRow("可用内存", _availableMemory), CreateInfoRow("频率", _memoryFreq) });
        memCard.Margin = new Thickness(2, 0, 2, 8);
        Grid.SetColumn(memCard, 1);

        var gpuCard = CreateInfoCard("显卡信息", new[] { CreateInfoRow("显卡", _gpuInfo), CreateInfoRow("显存", _gpuMemory) });
        gpuCard.Margin = new Thickness(4, 0, 0, 8);
        Grid.SetColumn(gpuCard, 2);

        topRow.Children.Add(cpuCard);
        topRow.Children.Add(memCard);
        topRow.Children.Add(gpuCard);
        stackPanel.Children.Add(topRow);

        var chartCard = new Border { Background = _cardBgBrush, BorderBrush = _cardBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 12) };
        var chartGrid = new Grid();
        chartGrid.ColumnDefinitions.Add(new ColumnDefinition());
        chartGrid.ColumnDefinitions.Add(new ColumnDefinition());
        chartGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var cpuChart = new UsageChart("CPU", WColor.FromArgb(255, 0, 120, 212));
        Grid.SetColumn(cpuChart, 0);
        var memChart = new UsageChart("内存", WColor.FromArgb(255, 16, 137, 62));
        Grid.SetColumn(memChart, 1);
        var gpuChart = new UsageChart("GPU", WColor.FromArgb(255, 196, 43, 28));
        Grid.SetColumn(gpuChart, 2);

        chartGrid.Children.Add(cpuChart);
        chartGrid.Children.Add(memChart);
        chartGrid.Children.Add(gpuChart);
        chartCard.Child = chartGrid;
        stackPanel.Children.Add(chartCard);

        stackPanel.Children.Add(CreateInfoCard("硬盘信息", new[] { CreateInfoRow("硬盘", _diskInfo), CreateInfoRow("型号", _diskModel) }));
        stackPanel.Children.Add(CreateInfoCard("系统信息", new[] { CreateInfoRow("操作系统", _osInfo), CreateInfoRow("计算机名", Environment.MachineName), CreateInfoRow("用户名", Environment.UserName), CreateInfoRow("本机IP", _localIP), CreateInfoRow("声卡", _audioInfo), CreateInfoRow("显示器", _monitorInfo) }));

        _systemInfoCache = stackPanel;
        contentArea.Children.Add(stackPanel);
        StartChartTimer(cpuChart, memChart, gpuChart);
    }

    private void StartChartTimer(UsageChart cpuChart, UsageChart memChart, UsageChart gpuChart)
    {
        _chartTimer?.Stop();

        if (_gpuCounters != null) { foreach (var pc in _gpuCounters) pc.Dispose(); }
        _gpuCounters = [];
        try
        {
            if (PerformanceCounterCategory.Exists("GPU Engine"))
            {
                var instances = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                    .Where(n => n.Contains("engtype_3D") || n.Contains("engtype_Graphics") || n.Contains("engtype_VideoProcessing") || n.Contains("engtype_Compute"));
                foreach (var inst in instances)
                    _gpuCounters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", inst));
            }
        }
        catch { }

        _chartTimer = DispatcherQueue.CreateTimer();
        _chartTimer.Interval = TimeSpan.FromSeconds(1);
        _chartTimer.Tick += (s, e) =>
        {
            try
            {
                cpuChart.AddValue(_cpuCounter?.NextValue() ?? 0);
                float memVal = 0;
                try
                {
                    using var os = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in os.Get())
                    {
                        ulong total = Convert.ToUInt64(obj["TotalVisibleMemorySize"]);
                        ulong free = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                        if (total > 0) memVal = (float)((double)(total - free) / total * 100);
                    }
                }
                catch { }
                memChart.AddValue(memVal);

                float gpuVal = 0;
                try
                {
                    if (_gpuCounters.Count > 0)
                    {
                        float total = 0;
                        foreach (var pc in _gpuCounters)
                            total += pc.NextValue();
                        gpuVal = Math.Min(total, 100);
                    }
                    else
                    {
                        using var gpu = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_VideoController");
                        foreach (ManagementObject obj in gpu.Get())
                        {
                            var load = obj["LoadPercentage"];
                            if (load != null) gpuVal = Convert.ToSingle(load);
                        }
                    }
                }
                catch { }
                gpuChart.AddValue(gpuVal);
            }
            catch { }
        };
        _chartTimer.Start();
    }

    private Border CreateInfoCard(string title, UIElement[] infoRows)
    {
        var card = new Border
        {
            Background = _cardBgBrush,
            BorderBrush = _cardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var row in infoRows) sp.Children.Add(row);
        card.Child = sp;
        return card;
    }

    private Grid CreateInfoRow(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, Foreground = _textSecondaryBrush, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var val = new TextBlock { Text = value, Foreground = _textPrimaryBrush, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);
        return grid;
    }

    #endregion

    #region 系统信息获取

    private string GetCPUInfo()
    {
        try { using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"); foreach (ManagementObject o in s.Get()) { var n = o["Name"]; if (n != null) return n.ToString().Trim(); } } catch { }
        return "未知";
    }

    private string GetCPUCores()
    {
        try { using var s = new ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"); foreach (ManagementObject o in s.Get()) { var c = o["NumberOfCores"]; var t = o["NumberOfLogicalProcessors"]; if (c != null && t != null) return $"{c}核心 {t}线程"; } } catch { }
        return Environment.ProcessorCount + "逻辑处理器";
    }

    private string GetCPUUsage() => "图表显示";

    private string GetCPUFreq()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT MaxClockSpeed, CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementObject o in s.Get())
            {
                var max = o["MaxClockSpeed"]?.ToString();
                var cur = o["CurrentClockSpeed"]?.ToString();
                if (!string.IsNullOrEmpty(max))
                {
                    var result = $"{Convert.ToUInt64(max) / 1000.0:F2} GHz";
                    if (!string.IsNullOrEmpty(cur) && cur != max && cur != "0")
                        result += $" (当前 {Convert.ToUInt64(cur) / 1000.0:F2} GHz)";
                    return result;
                }
            }
        }
        catch { }
        return "未知";
    }

    private string GetTotalMemory()
    {
        try { using var s = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"); ulong t = 0; foreach (ManagementObject o in s.Get()) { var c = o["Capacity"]; if (c != null) t += Convert.ToUInt64(c); } if (t > 0) return $"{t / (1024 * 1024 * 1024)} GB"; } catch { }
        try { using var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"); foreach (ManagementObject o in s.Get()) { var m = o["TotalVisibleMemorySize"]; if (m != null) return $"{Convert.ToUInt64(m) / (1024 * 1024)} GB"; } } catch { }
        return "未知";
    }

    private string GetAvailableMemory()
    {
        try { using var s = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem"); foreach (ManagementObject o in s.Get()) { var f = o["FreePhysicalMemory"]; if (f != null) return $"{Convert.ToUInt64(f) / (1024 * 1024):F1} GB"; } } catch { }
        return "未知";
    }

    private string GetMemoryUsage() => "图表显示";

    private string GetMemoryFreq()
    {
        try
        {
            var speeds = new HashSet<string>();
            using var s = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory");
            foreach (ManagementObject o in s.Get())
            {
                var sp = o["Speed"]?.ToString();
                if (!string.IsNullOrEmpty(sp) && sp != "0")
                    speeds.Add($"{sp} MHz");
            }
            if (speeds.Count > 0) return string.Join(" / ", speeds);
        }
        catch { }
        return "未知";
    }

    private string GetGPUInfo()
    {
        try
        {
            var gpus = new List<string>();
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject o in s.Get())
            {
                var n = o["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(n) && !n.Contains("Basic") && !n.Contains("Microsoft") && !n.Contains("Remote"))
                    gpus.Add(n);
            }
            if (gpus.Count > 0) return string.Join(" / ", gpus);
        }
        catch { }
        return "未知";
    }

    private string GetGPUMemory()
    {
        try { using var s = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController"); foreach (ManagementObject o in s.Get()) { var r = o["AdapterRAM"]; if (r != null && r.ToString() != "0") return $"{Convert.ToUInt64(r) / (1024 * 1024)} MB"; } } catch { }
        return "未知";
    }

    private string GetDiskInfo()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var d in DriveInfo.GetDrives())
                if (d.IsReady && d.DriveType == DriveType.Fixed)
                    sb.Append($"{d.Name} {d.TotalSize / (1024 * 1024 * 1024)}GB(可用{d.AvailableFreeSpace / (1024 * 1024 * 1024)}GB) ");
            if (sb.Length > 0) return sb.ToString().Trim();
        }
        catch { }
        return "未知";
    }

    private string GetDiskModel()
    {
        try
        {
            var disks = new List<string>();
            using var s = new ManagementObjectSearcher("SELECT Model, InterfaceType, MediaType FROM Win32_DiskDrive");
            foreach (ManagementObject o in s.Get())
            {
                var model = o["Model"]?.ToString()?.Trim();
                var iface = o["InterfaceType"]?.ToString();
                if (!string.IsNullOrEmpty(model))
                {
                    var label = model;
                    if (!string.IsNullOrEmpty(iface) && iface != "IDE")
                        label += $" ({iface})";
                    disks.Add(label);
                }
            }
            if (disks.Count > 0) return string.Join(" / ", disks);
        }
        catch { }
        return "未知";
    }

    private string GetOSInfo()
    {
        try { using var s = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"); foreach (ManagementObject o in s.Get()) { var c = o["Caption"]; if (c != null) return c.ToString().Trim(); } } catch { }
        return Environment.OSVersion.VersionString;
    }

    private string GetAudioInfo()
    {
        try
        {
            var names = new List<string>();
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_SoundDevice");
            foreach (ManagementObject o in s.Get())
            {
                var n = o["Name"];
                if (n != null) names.Add(n.ToString().Trim());
            }
            return names.Count > 0 ? string.Join(", ", names) : "未知";
        }
        catch { return "未知"; }
    }

    private string GetMonitorInfo()
    {
        try
        {
            var names = new List<string>();
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_DesktopMonitor");
            foreach (ManagementObject o in s.Get())
            {
                var n = o["Name"];
                if (n != null) names.Add(n.ToString().Trim());
            }
            return names.Count > 0 ? string.Join(", ", names) : "未知";
        }
        catch { return "未知"; }
    }

    private string GetLocalIPAddress()
    {
        try
        {
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
            return ips.Count > 0 ? string.Join(", ", ips) : "未知";
        }
        catch { return "未知"; }
    }

    private PerformanceCounter TryCreateCpuCounter()
    {
        try { return new PerformanceCounter("Processor", "% Processor Time", "_Total"); } catch { return null; }
    }

    private void DisableDoubleClickMaximize()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _wndProcDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCLBUTTONDBLCLK && (int)wParam == HTCAPTION)
            return IntPtr.Zero; // 禁止双击标题栏最大化
        try { return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam); }
        catch { return IntPtr.Zero; }
    }

    internal List<ToolInfo> GetTools() => _allTools;
    internal void SetTools(List<ToolInfo> tools) { _allTools = tools; SaveTools(); }
    internal void ResetTools() { InitDefaultTools(); }
    internal void RefreshCurrentPage() { LoadContent(GetCurrentTag()); }
    internal XamlRoot GetContentXamlRoot() => contentArea.XamlRoot;

    #endregion
}
