using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
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
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 1;
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1;
    private const uint NIF_ICON = 2;
    private const uint NIF_TIP = 4;
    private const uint NIF_SHOWTIP = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint cmd, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const uint MF_STRING = 0;
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_LEFTALIGN = 0;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private IntPtr _oldWndProc;
    private WndProcDelegate _wndProcDelegate;
    private NOTIFYICONDATA _trayData;
    private IntPtr _trayMenu;
    private bool _trayIconAdded;
    private IntPtr _appHwnd;
    private const uint TRAY_SHOW = 1001;
    private const uint TRAY_EXIT = 1002;

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
    private static readonly string ConfigVersionFile = Path.Combine(DataDir, "config.version");
    private static readonly string CurrentConfigVersion = "1.1.0";

    private NavigationViewItem _navProxyItem;
    private NavigationViewItem _navAcceleratorItem;
    private FileSystemWatcher _proxyWatcher;
    private List<ToolInfo>? _cachedStorePlugins;
    private static readonly string ProxyExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProxyTools", "mihomo", "mihomo.exe");
    private static readonly string ProxyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProxyTools");
    private static readonly string AcceleratorExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AcceleratorHelper", "AcceleratorHelper.exe");

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
        AppWindow.Closing += (s, e) =>
        {
            e.Cancel = true;
            MinimizeToTray();
        };

        Activated += async (s, e) =>
        {
            if (_loaded) return;
            _loaded = true;
            UpdateProxyNavItem();
            UpdateAcceleratorNavItem();
            SetupProxyWatcher();
            _ = FetchRemotePluginsAsync().ContinueWith(t =>
            {
                if (t.Result.Count > 0)
                    _cachedStorePlugins = t.Result;
            }, TaskScheduler.Default);
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
    private readonly Stack<string> _navHistory = new();
    private bool _suppressNavHistory;
    private CancellationTokenSource _pingCts;
    private CancellationTokenSource _splitCts;

    private void PushNavHistory(string tag)
    {
        if (_navHistory.Count == 0 || _navHistory.Peek() != tag)
            _navHistory.Push(tag);
    }

    private void Cleanup()
    {
        _chartTimer?.Stop();
        _cpuCounter?.Dispose();
        if (_gpuCounters != null)
            foreach (var pc in _gpuCounters) pc.Dispose();
        _proxyWatcher?.Dispose();
        DestroyTrayIcon();
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
                if (_currentTag == "settings")
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
            if (!Directory.Exists(DataDir))
                Directory.CreateDirectory(DataDir);

            var storedVersion = "";
            try { storedVersion = File.ReadAllText(ConfigVersionFile).Trim(); } catch { }

            if (storedVersion != CurrentConfigVersion)
            {
                try { File.Delete(ToolsFile); } catch { }
                try { File.Delete(FavoritesFile); } catch { }
                try { File.WriteAllText(ConfigVersionFile, CurrentConfigVersion); } catch { }
            }

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

    private static string GetToolDescription(string name)
    {
        return name switch
        {
            "CPUZ" or "CPU-Z" => "CPU 型号/频率/缓存详细检测",
            "CoreTemp" => "CPU 核心温度实时监控",
            "LinX" => "CPU 与内存稳定性压力测试",
            "Prime95" => "CPU 极限压力与稳定性测试",
            "superpi" => "CPU 单核性能基准测试（圆周率计算）",
            "wPrime" => "CPU 多核性能基准测试",
            "ThrottleStop" => "CPU 降压降温与性能调优",
            "XIANGQI" or "象棋" => "CPU 多核性能基准测试（象棋引擎）",
            "iva" => "Intel 处理器诊断工具",
            "FurMark" => "GPU 极限压力与稳定性测试（甜甜圈）",
            "memtest" or "memtest64" or "memtestpro" => "内存稳定性与错误检测",
            "tm5" => "内存稳定性压力测试（TestMem5）",
            "Thaiphoon" => "内存 SPD 信息读取与编辑",
            "魔方内存盘" => "内存虚拟硬盘加速工具",
            "DDU" => "显卡驱动彻底卸载与清理",
            "GPUZ" or "GPU-Z" => "显卡型号/频率/传感器详细检测",
            "nvidiaInspector" => "NVIDIA 显卡超频与参数监控",
            "nvidiaProfileInspector" => "NVIDIA 驱动配置文件管理",
            "dxvachecker" => "DirectX 功能支持检测",
            "GpuTest_Windows x64" or "GpuTest" => "GPU 跨平台基准性能测试",
            "AMD显卡驱动下载" => "AMD 显卡驱动在线安装",
            "Nvidia显卡驱动下载" => "NVIDIA 显卡驱动在线安装",
            "CrystalDiskInfo" => "硬盘 S.M.A.R.T. 状态检测与健康评估",
            "CrystalDiskMark" => "硬盘读写速度基准测试",
            "ASSSDBenchmark" => "SSD 固态硬盘读写性能测试",
            "ATTODISKBENCHMARK" => "硬盘连续传输速度基准测试",
            "TxBENCH" => "SSD 读写性能测试与 Trim 检测",
            "HDTune" => "硬盘健康/基准/错误扫描综合检测",
            "DiskGenius" => "硬盘分区管理与数据恢复",
            "Defraggler" => "磁盘碎片分析与整理",
            "finaldata" => "误删文件数据恢复",
            "魔方数据恢复" => "误删除文件快速恢复",
            "WizTree" => "磁盘空间占用分析（极速）",
            "windirstat" => "磁盘空间占用可视化分析",
            "SpaceSniffer" => "磁盘空间占用树状图分析",
            "H2testw" => "U盘/SD 卡扩容骗局检测与完整性验证",
            "mydisktest" => "U盘/SD 卡真实容量与速度检测",
            "URWTEST" => "USB 存储设备读写稳定性测试",
            "FlashMaster" => "U盘主控信息检测与量产工具",
            "LLFTOOL" => "硬盘低级格式化（低格）",
            "SSDZ" => "SSD 固态硬盘详细信息检测",
            "SSD utils" => "SSD 读写优化与维护工具",
            "AresonMouseTest" => "鼠标按键响应与灵敏度测试",
            "Keyboard Test Utility" => "键盘按键功能完整性测试",
            "KeyTweak" => "键盘按键映射自定义修改",
            "MOUSERATE" => "鼠标回报率与响应速度测试",
            "MouseTester" => "鼠标丢帧与精准度分析",
            "鼠标单机变双击测试器" => "鼠标微动双击故障检测",
            "色域检测" => "显示器色域覆盖率检测",
            "在线屏幕测试" => "显示器坏点/漏光/灰度在线检测",
            "UFO测试" => "显示器刷新率与拖影在线测试",
            "AIDA64" => "全面硬件信息检测与稳定性测试",
            "hwinfo" or "HWiNFO" => "深度硬件传感器信息监控",
            "HWMonitor" => "硬件温度/电压/风扇转速监控",
            "speccy" => "系统硬件信息快速查看",
            "RWEverything" => "硬件寄存器读取与低级访问",
            "BatteryInfoView" => "笔记本电池循环次数与健康度检测",
            "bluescreenview" => "蓝屏崩溃日志分析与查看",
            "DesktopOK" => "桌面图标布局备份与恢复",
            "DirectX_Repair" => "DirectX 运行时组件修复",
            "Dism++" => "系统镜像管理、优化与修复",
            "Everything" => "本地文件秒级快速搜索",
            "Geek Uninstaller" => "软件彻底卸载与残留清理",
            "gifcam" => "屏幕区域 GIF 动态录制",
            "MSIAfterburnerSetup" => "显卡超频与游戏内硬件监控",
            "next_itellyou" => "微软官方系统镜像下载查询",
            "procexp" or "Process Explorer" => "高级进程管理与排查",
            "rufus" => "启动盘制作工具（U盘 ISO 写入）",
            "ULTRAISO" => "光盘镜像编辑与制作",
            "ventoy" => "多系统启动 U盘 制作工具",
            "WinDbg" => "Windows 内核级调试分析",
            "WTGA" => "Windows To Go 便携系统制作",
            "皮肤编辑器" => "软件界面皮肤自定义编辑",
            "天梯图" => "硬件性能排行天梯图查看",
            "游戏加加" => "游戏内硬件监控与性能优化",
            _ => "硬件检测与系统工具"
        };
    }

    private void MergeScannedTools()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var toolsDir = Path.Combine(baseDir, "tools");

        // 更新已有工具的描述
        bool changed = false;
        foreach (var tool in _allTools)
        {
            var newDesc = GetToolDescription(tool.Name);
            if (tool.Description != newDesc)
            {
                tool.Description = newDesc;
                changed = true;
            }
        }

        if (!Directory.Exists(toolsDir)) { if (changed) SaveTools(); return; }

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
                    Description = GetToolDescription(toolName),
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
                            Description = GetToolDescription(toolName),
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
            "代理工具" => "代理工具",
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
            if (tag != _currentTag && !_suppressNavHistory)
                PushNavHistory(_currentTag);
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

        backBtn.Visibility = _navHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
            case "proxy":
                if (File.Exists(ProxyExePath))
                {
                    navView.Header = "代理工具";
                    ShowProxyTools();
                }
                else
                {
                    UpdateProxyNavItem();
                    navView.SelectedItem = navSystem;
                    LoadContent("system");
                }
                break;
            case "accelerator":
                if (File.Exists(AcceleratorExePath))
                {
                    navView.Header = "网络加速";
                    ShowAcceleratorPage();
                }
                else
                {
                    UpdateAcceleratorNavItem();
                    navView.SelectedItem = navStore;
                    LoadContent("工具商店");
                }
                break;
            default:
                navView.Header = tag;
                if (tag == "工具商店")
                    ShowStoreAsync();
                else
                    ShowCategory(tag);
                break;
        }
    }

    private void UpdateProxyNavItem()
    {
        var menuItems = navView.MenuItems;
        bool exists = File.Exists(ProxyExePath);
        bool hasItem = _navProxyItem != null && menuItems.Contains(_navProxyItem);

        if (exists && !hasItem)
        {
            _navProxyItem = new NavigationViewItem
            {
                Content = "代理工具",
                Tag = "proxy",
                Icon = new FontIcon { Glyph = "\xEE47", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"] }
            };
            int insertIndex = 0;
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (menuItems[i] is NavigationViewItem item && item.Tag?.ToString() == "network")
                {
                    insertIndex = i + 1;
                    break;
                }
            }
            menuItems.Insert(insertIndex, _navProxyItem);
        }
        else if (!exists && hasItem)
        {
            menuItems.Remove(_navProxyItem);
            _navProxyItem = null;
        }
    }

    private void UpdateAcceleratorNavItem()
    {
        var menuItems = navView.MenuItems;
        bool exists = File.Exists(AcceleratorExePath);
        bool hasItem = _navAcceleratorItem != null && menuItems.Contains(_navAcceleratorItem);

        if (exists && !hasItem)
        {
            _navAcceleratorItem = new NavigationViewItem
            {
                Content = "网络加速",
                Tag = "accelerator",
                Icon = new FontIcon { Glyph = "\xE774", FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"] }
            };
            int insertIndex = 0;
            for (int i = 0; i < menuItems.Count; i++)
            {
                if (menuItems[i] is NavigationViewItem item && item.Tag?.ToString() == "network")
                {
                    insertIndex = i + 1;
                    break;
                }
            }
            menuItems.Insert(insertIndex, _navAcceleratorItem);
        }
        else if (!exists && hasItem)
        {
            menuItems.Remove(_navAcceleratorItem);
            _navAcceleratorItem = null;
        }
    }

    private void SetupProxyWatcher()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _proxyWatcher = new FileSystemWatcher(baseDir)
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = true
            };
            _proxyWatcher.Created += (s, e) =>
            {
                if (e.FullPath.IndexOf("ProxyTools\\mihomo\\mihomo.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    DispatcherQueue.TryEnqueue(() => UpdateProxyNavItem());
                if (e.FullPath.IndexOf("AcceleratorHelper\\AcceleratorHelper.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    DispatcherQueue.TryEnqueue(() => UpdateAcceleratorNavItem());
            };
            _proxyWatcher.Deleted += (s, e) =>
            {
                if (e.FullPath.IndexOf("ProxyTools\\mihomo\\mihomo.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    DispatcherQueue.TryEnqueue(() => UpdateProxyNavItem());
                if (e.FullPath.IndexOf("AcceleratorHelper\\AcceleratorHelper.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    DispatcherQueue.TryEnqueue(() => UpdateAcceleratorNavItem());
            };
            _proxyWatcher.Renamed += (s, e) =>
            {
                if (e.FullPath.IndexOf("ProxyTools\\mihomo\\mihomo.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    DispatcherQueue.TryEnqueue(() => UpdateProxyNavItem());
                if (e.FullPath.IndexOf("AcceleratorHelper\\AcceleratorHelper.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    DispatcherQueue.TryEnqueue(() => UpdateAcceleratorNavItem());
            };
        }
        catch { }
    }

    #endregion

    #region 页面展示

    private void ShowFavorites()
    {
        var favs = _allTools.Where(t => IsFavorite(t)).ToList();
        if (_cachedStorePlugins != null)
            favs.AddRange(_cachedStorePlugins.Where(t => IsFavorite(t)));
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

    private async void ShowStoreAsync()
    {
        if (_cachedStorePlugins == null)
            _cachedStorePlugins = await FetchRemotePluginsAsync();

        if (_cachedStorePlugins.Count > 0)
        {
            contentArea.Children.Add(CreateToolGrid(_cachedStorePlugins));
        }
        else
        {
            contentArea.Children.Add(new TextBlock
            {
                Text = "加载商店失败，请检查网络后重试",
                FontSize = 16,
                Foreground = _textSecondaryBrush,
                Margin = new Thickness(0, 40, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    private static readonly string PluginsJsonUrl = "https://raw.githubusercontent.com/bnganblog/XKBTOOL/master/plugin/plugins.json";

    private async Task<List<ToolInfo>> FetchRemotePluginsAsync()
    {
        try
        {
            var proxyPrefix = App.DownloadProxy;
            var urls = string.IsNullOrEmpty(proxyPrefix)
                ? new[] { PluginsJsonUrl }
                : new[] { proxyPrefix + PluginsJsonUrl, PluginsJsonUrl };

            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XKBToolbox");
            string? json = null;
            foreach (var url in urls)
            {
                try
                {
                    json = await client.GetStringAsync(url);
                    break;
                }
                catch { }
            }
            if (json == null) return [];
            var items = JsonSerializer.Deserialize<List<ToolInfo>>(json);
            if (items == null) return [];
            foreach (var item in items)
            {
                item.Category = "工具商店";
                item.Action = $"dl:{item.Name}";
            }
            return items;
        }
        catch
        {
            return [];
        }
    }

    private void ShowProxyTools()
    {
        contentArea.Children.Add(new ProxyTools.ProxyPage());
    }

    private void ShowAcceleratorPage()
    {
        contentArea.Children.Add(new Accelerator.AcceleratorPage(this));
    }

    private void ShowNetworkTools()
    {
        var stackPanel = new StackPanel();

        // 选项卡
        var tabBar = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        tabBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
        var speedTabBtn = new Button
        {
            Content = "网速测试",
            FontSize = 15,
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0)),
            Foreground = _textSecondaryBrush,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(ipTabBtn, 0);
        Grid.SetColumn(splitTabBtn, 1);
        Grid.SetColumn(speedTabBtn, 2);
        tabBar.Children.Add(ipTabBtn);
        tabBar.Children.Add(splitTabBtn);
        tabBar.Children.Add(speedTabBtn);
        stackPanel.Children.Add(tabBar);

        var ipPanel = new StackPanel();
        BuildIPTab(ipPanel);
        stackPanel.Children.Add(ipPanel);

        var splitPanel = new StackPanel { Visibility = Visibility.Collapsed };
        BuildSplitTab(splitPanel);
        stackPanel.Children.Add(splitPanel);

        var speedPanel = new StackPanel { Visibility = Visibility.Collapsed };
        BuildSpeedTab(speedPanel);
        stackPanel.Children.Add(speedPanel);

        contentArea.Children.Add(stackPanel);

        ipTabBtn.Click += (s, e) => SwitchTab3(ipTabBtn, splitTabBtn, speedTabBtn, ipPanel, splitPanel, speedPanel);
        splitTabBtn.Click += (s, e) => SwitchTab3(splitTabBtn, ipTabBtn, speedTabBtn, splitPanel, ipPanel, speedPanel);
        speedTabBtn.Click += (s, e) => SwitchTab3(speedTabBtn, ipTabBtn, splitTabBtn, speedPanel, ipPanel, splitPanel);
    }

    private void SwitchTab3(Button active, Button inactive1, Button inactive2, Panel activePanel, Panel inactivePanel1, Panel inactivePanel2)
    {
        active.Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212));
        active.Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255));
        inactive1.Background = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0));
        inactive1.Foreground = _textSecondaryBrush;
        inactive2.Background = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0));
        inactive2.Foreground = _textSecondaryBrush;
        activePanel.Visibility = Visibility.Visible;
        inactivePanel1.Visibility = Visibility.Collapsed;
        inactivePanel2.Visibility = Visibility.Collapsed;
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
            ("Cloudflare", "国际", "https://www.cloudflare.com/cdn-cgi/trace"),
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
            FontSize = 18,
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        pingRefreshBtn.Content = new FontIcon { Glyph = "\uE72C", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 };
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
        var pingGrid1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        var domesticCards = AddPingCards(pingGrid1, domesticTargets);
        pingStack.Children.Add(pingGrid1);

        pingStack.Children.Add(new TextBlock { Text = "国际", FontSize = 14, Foreground = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var intlTargets = new[] {
            ("GitHub", "github.com", (string)null),
            ("jsDelivr", "cdn.jsdelivr.net", (string)null),
            ("Cloudflare", "cloudflare.com", (string)null),
            ("YouTube", "youtube.com", (string)null)
        };
        var pingGrid2 = new Grid();
        var intlCards = AddPingCards(pingGrid2, intlTargets);
        pingStack.Children.Add(pingGrid2);

        pingCard.Child = pingStack;
        parent.Children.Add(pingCard);

        _ = QueryIPInfo(ip4Val, ip4LocVal, ip4IspVal, null, ip6Val, ip6LocVal, ip6IspVal);
        for (int i = 0; i < sources.Length; i++)
        {
            var idx = i;
            _ = QuerySourceIP(sources[idx].url, sourceLabels[idx]);
        }
        async Task RunAllPings(CancellationToken ct)
        {
            // 重置所有圆点
            foreach (var c in domesticCards.Concat(intlCards))
                foreach (var d in c.Dots)
                    d.Fill = new SolidColorBrush(WColor.FromArgb(255, 200, 200, 200));
            await RunPingTestsDetailed(domesticTargets, domesticCards, ct);
            if (!ct.IsCancellationRequested)
                await RunPingTestsDetailed(intlTargets, intlCards, ct);
        }

        _pingCts = new CancellationTokenSource();
        _ = RunAllPings(_pingCts.Token);
        pingRefreshBtn.Click += async (s, e) =>
        {
            _pingCts?.Cancel();
            _pingCts = new CancellationTokenSource();
            await RunAllPings(_pingCts.Token);
        };
    }

    private void BuildSplitTab(StackPanel parent)
    {
        var refreshBtn = new Button
        {
            FontSize = 14,
            Padding = new Thickness(16, 6, 16, 6),
            Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var btnContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        btnContent.Children.Add(new FontIcon { Glyph = "\uE72C", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 });
        btnContent.Children.Add(new TextBlock { Text = "测试全部", VerticalAlignment = VerticalAlignment.Center });
        refreshBtn.Content = btnContent;
        parent.Children.Add(refreshBtn);

        var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var tablePanel = new StackPanel();
        scrollViewer.Content = tablePanel;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        header.Children.Add(new TextBlock { Text = "网站", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = _textSecondaryBrush });
        var ipHeader = new TextBlock { Text = "出口 IP", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = _textSecondaryBrush };
        Grid.SetColumn(ipHeader, 1);
        header.Children.Add(ipHeader);
        var locHeader = new TextBlock { Text = "归属地", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = _textSecondaryBrush };
        Grid.SetColumn(locHeader, 2);
        header.Children.Add(locHeader);
        tablePanel.Children.Add(header);

        var sites = new (string name, string echoUrl, string tag, string icon)[]
        {

            // 国内
            ("Cloudflare 中国", "https://www.cloudflare.com/cdn-cgi/trace", "国内", "cloudflare.png"),
            // Cloudflare 各节点（国际）
            ("Cloudflare 香港", "https://www.cloudflare.com/cdn-cgi/trace", "国际", "cloudflare.png"),
            ("Cloudflare 日本", "https://www.cloudflare.com/cdn-cgi/trace", "国际", "cloudflare.png"),
            ("Cloudflare 美国", "https://www.cloudflare.com/cdn-cgi/trace", "国际", "cloudflare.png"),
            ("Cloudflare 英国", "https://www.cloudflare.com/cdn-cgi/trace", "国际", "cloudflare.png"),
            // AI
            ("ChatGPT", "https://chatgpt.com/cdn-cgi/trace", "AI", "chatgpt.png"),
            ("Claude", "https://claude.ai/cdn-cgi/trace", "AI", null),
            ("Perplexity", "https://perplexity.ai/cdn-cgi/trace", "AI", null),
            // 开发
            ("jsDelivr", "https://cdn.jsdelivr.net/cdn-cgi/trace", "开发", "jsdelivr.svg"),
            ("GitHub", "https://github.com/cdn-cgi/trace", "开发", null),
            // 社区
            ("X (Twitter)", "https://x.com/cdn-cgi/trace", "社区", "x.png"),
            ("Discord", "https://discord.com/cdn-cgi/trace", "社区", "discord.png"),
            ("Medium", "https://medium.com/cdn-cgi/trace", "社区", "medium.png"),
            // 加密货币
            ("Coinbase", "https://coinbase.com/cdn-cgi/trace", "Crypto", "coinbase.svg"),
            ("OKX", "https://okx.com/cdn-cgi/trace", "Crypto", "okx.png"),
            // 游戏
            ("Epic Games", "https://epicgames.com/cdn-cgi/trace", "游戏", "epicgames.png"),
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
                        : currentGroup == "开发" ? new SolidColorBrush(WColor.FromArgb(255, 100, 100, 200))
                        : currentGroup == "社区" ? new SolidColorBrush(WColor.FromArgb(255, 200, 100, 50))
                        : currentGroup == "Crypto" ? new SolidColorBrush(WColor.FromArgb(255, 150, 80, 200))
                        : new SolidColorBrush(WColor.FromArgb(255, 0, 150, 136)),
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
            var iconFile = sites[i].icon;
            var iconPath = iconFile != null ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", iconFile) : null;
            if (iconPath != null && File.Exists(iconPath))
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

        async Task RunSplitTest(CancellationToken ct)
        {
            refreshBtn.Content = "测试中...";
            var tasks = sites.Select(async (site, i) =>
            {
                if (ct.IsCancellationRequested) return;
                siteLabels[i].ip.Text = "检测中...";
                siteLabels[i].loc.Text = "";
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(10000);
                    using var client = new System.Net.Http.HttpClient();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                    var resp = await client.GetStringAsync(site.echoUrl, cts.Token);
                    string ip = null;
                    if (site.echoUrl.Contains("cdn-cgi/trace"))
                    {
                        var match = Regex.Match(resp, @"ip=(\S+)");
                        ip = match.Success ? match.Groups[1].Value : null;
                    }
                    else
                    {
                        ip = "可访问";
                        _ = Task.Run(() =>
                        {
                            try
                            {
                                var domain = new Uri(site.echoUrl).Host;
                                var addrs = Dns.GetHostAddresses(domain);
                                var dnsIp = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                                if (dnsIp != null)
                                    DispatcherQueue.TryEnqueue(() => siteLabels[i].ip.Text = dnsIp.ToString());
                            }
                            catch { }
                        }, ct);
                    }
                    if (ip != null)
                    {
                        DispatcherQueue.TryEnqueue(() => siteLabels[i].ip.Text = ip);
                        _ = QueryGeo(ip, siteLabels[i].loc);
                    }
                    else
                        DispatcherQueue.TryEnqueue(() => siteLabels[i].ip.Text = "无结果");
                }
                catch
                {
                    DispatcherQueue.TryEnqueue(() => siteLabels[i].ip.Text = "超时/阻断");
                }
            });
            await Task.WhenAll(tasks);
            var doneContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            doneContent.Children.Add(new FontIcon { Glyph = "\uE72C", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 });
            doneContent.Children.Add(new TextBlock { Text = "测试全部", VerticalAlignment = VerticalAlignment.Center });
            refreshBtn.Content = doneContent;
        }

        _splitCts = new CancellationTokenSource();
        _ = RunSplitTest(_splitCts.Token);
        refreshBtn.Click += async (s, e) =>
        {
            _splitCts?.Cancel();
            _splitCts = new CancellationTokenSource();
            await RunSplitTest(_splitCts.Token);
        };
    }

    private void BuildSpeedTab(StackPanel parent)
    {
        var cfBase = "https://speed.cloudflare.com";

        var card = new Border { Background = _cardBgBrush, BorderBrush = _cardBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 12) };
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(new TextBlock { Text = "网速测试", FontSize = 22, FontWeight = FontWeights.Bold });

        var statusText = new TextBlock { Text = "点击下方按钮开始测试", FontSize = 14, Foreground = _textSecondaryBrush };

        // 延迟 / 下载 / 上传 结果卡片
        var resultGrid = new Grid();
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition());
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition());
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var latencyPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        latencyPanel.Children.Add(new TextBlock { Text = "延迟", FontSize = 13, Foreground = _textSecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center });
        var latencyVal = new TextBlock { Text = "-- ms", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = _textPrimaryBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        latencyPanel.Children.Add(latencyVal);
        resultGrid.Children.Add(latencyPanel);
        var dlPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        dlPanel.Children.Add(new TextBlock { Text = "下载", FontSize = 13, Foreground = _textSecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center });
        var dlVal = new TextBlock { Text = "-- Mbps", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = _textPrimaryBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        dlPanel.Children.Add(dlVal);
        Grid.SetColumn(dlPanel, 1);
        resultGrid.Children.Add(dlPanel);
        var ulPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        ulPanel.Children.Add(new TextBlock { Text = "上传", FontSize = 13, Foreground = _textSecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center });
        var ulVal = new TextBlock { Text = "-- Mbps", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = _textPrimaryBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        ulPanel.Children.Add(ulVal);
        Grid.SetColumn(ulPanel, 2);
        resultGrid.Children.Add(ulPanel);
        stack.Children.Add(resultGrid);

        // 下载实时速度图表
        var chartLabel = new TextBlock { Text = "下载速度", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = _textSecondaryBrush, Margin = new Thickness(0, 8, 0, 4) };
        stack.Children.Add(chartLabel);
        var dlChartBorder = new Border
        {
            Background = new SolidColorBrush(WColor.FromArgb(20, 0, 120, 212)),
            CornerRadius = new CornerRadius(8),
            Height = 150,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var dlChartGrid = new Grid();
        var dlChartCanvas = new Canvas();
        var dlChartLine = new Microsoft.UI.Xaml.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        var dlChartFill = new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Fill = new SolidColorBrush(WColor.FromArgb(60, 0, 120, 212)),
        };
        dlChartCanvas.Children.Add(dlChartFill);
        dlChartCanvas.Children.Add(dlChartLine);
        dlChartGrid.Children.Add(dlChartCanvas);
        var dlSpeedOverlay = new TextBlock
        {
            Text = "-- Mbps",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 12, 0)
        };
        dlChartGrid.Children.Add(dlSpeedOverlay);
        dlChartBorder.Child = dlChartGrid; // 改为用 Grid 包装
        stack.Children.Add(dlChartBorder);

        // 上传实时速度图表
        var ulChartLabel = new TextBlock { Text = "上传速度", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = _textSecondaryBrush, Margin = new Thickness(0, 8, 0, 4) };
        stack.Children.Add(ulChartLabel);
        var ulChartBorder = new Border
        {
            Background = new SolidColorBrush(WColor.FromArgb(20, 16, 137, 62)),
            CornerRadius = new CornerRadius(8),
            Height = 150,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var ulChartGrid = new Grid();
        var ulChartCanvas = new Canvas();
        var ulChartLine = new Microsoft.UI.Xaml.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(WColor.FromArgb(255, 16, 137, 62)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        var ulChartFill = new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Fill = new SolidColorBrush(WColor.FromArgb(60, 16, 137, 62)),
        };
        ulChartCanvas.Children.Add(ulChartFill);
        ulChartCanvas.Children.Add(ulChartLine);
        ulChartGrid.Children.Add(ulChartCanvas);
        var ulSpeedOverlay = new TextBlock
        {
            Text = "-- Mbps",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 16, 137, 62)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 12, 0)
        };
        ulChartGrid.Children.Add(ulSpeedOverlay);
        ulChartBorder.Child = ulChartGrid;
        stack.Children.Add(ulChartBorder);

        // 进度条
        var progressBar = new ProgressBar { Value = 0, Minimum = 0, Maximum = 100, Height = 6, Visibility = Visibility.Collapsed };
        stack.Children.Add(progressBar);
        stack.Children.Add(statusText);

        var startBtn = new Button
        {
            Content = "⟳ 开始测试",
            FontSize = 15,
            Padding = new Thickness(24, 10, 24, 10),
            Background = new SolidColorBrush(WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        stack.Children.Add(startBtn);

        card.Child = stack;
        parent.Children.Add(card);

        CancellationTokenSource speedCts = null;

        async Task RunSpeedTest(CancellationToken ct)
        {
            startBtn.IsEnabled = false;
            startBtn.Content = "测试中...";
            progressBar.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            latencyVal.Text = "-- ms";
            dlVal.Text = "-- Mbps";
            ulVal.Text = "-- Mbps";

            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

            try
            {
                // 延迟测试
                statusText.Text = "延迟测试中...";
                progressBar.Value = 5;
                using var ping = new System.Net.NetworkInformation.Ping();
                var pingReply = await ping.SendPingAsync("speed.cloudflare.com", 5000);
                if (pingReply.Status == System.Net.NetworkInformation.IPStatus.Success && !ct.IsCancellationRequested)
                {
                    latencyVal.Text = $"{pingReply.RoundtripTime} ms";
                    latencyVal.Foreground = pingReply.RoundtripTime < 50
                        ? new SolidColorBrush(WColor.FromArgb(255, 0, 180, 100))
                        : pingReply.RoundtripTime < 150
                            ? new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0))
                            : new SolidColorBrush(WColor.FromArgb(255, 220, 50, 50));
                }

                // 下载测试（分多阶段）
                if (!ct.IsCancellationRequested)
                {
                    statusText.Text = "下载测试中...";
                    dlChartLine.Points.Clear();
                    dlChartFill.Points.Clear();
                    dlChartCanvas.Width = dlChartBorder.ActualWidth > 0 ? dlChartBorder.ActualWidth : 600;
                    dlChartCanvas.Height = dlChartBorder.ActualHeight > 0 ? dlChartBorder.ActualHeight : 150;
                    var samples = new List<double>();
                    var sw = Stopwatch.StartNew();

                    var dlSizes = new long[] { 10 * 1024 * 1024, 25 * 1024 * 1024, 50 * 1024 * 1024 };
                    double totalDlMbps = 0;
                    int dlCount = 0;
                    DateTime lastSample = DateTime.UtcNow;
                    long lastTotalRead = 0;
                    long overallRead = 0;

                    for (int s = 0; s < dlSizes.Length && !ct.IsCancellationRequested; s++)
                    {
                        progressBar.Value = 5 + (s + 1) * 25;
                        statusText.Text = $"下载测试 ({s + 1}/{dlSizes.Length})...";
                        var resp = await client.GetStreamAsync($"{cfBase}/__down?bytes={dlSizes[s]}");
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        while (!ct.IsCancellationRequested)
                        {
                            var read = await resp.ReadAsync(buffer, 0, buffer.Length, ct);
                            if (read <= 0) break;
                            totalRead += read;
                            overallRead += read;

                            // 每秒采样
                            var now = DateTime.UtcNow;
                            var elapsed = (now - lastSample).TotalSeconds;
                            if (elapsed >= 0.2)
                            {
                                var bitsPerSec = (read * 8.0) / elapsed;
                                var sampleMbps = bitsPerSec / (1024.0 * 1024.0);
                                samples.Add(sampleMbps);
                                lastSample = now;
                                DispatcherQueue.TryEnqueue(() => dlSpeedOverlay.Text = $"{sampleMbps:F1} Mbps");
                                // 更新图表
                                var cw = dlChartCanvas.Width;
                                var ch = dlChartCanvas.Height;
                                var maxSpeed = samples.Max();
                                if (maxSpeed < 1) maxSpeed = 1;
                                var linePoints = new PointCollection();
                                var fillPoints = new PointCollection();
                                fillPoints.Add(new Point(10, ch - 10));
                                for (int k = 0; k < samples.Count; k++)
                                {
                                    var x = samples.Count > 1 ? (k / (double)(samples.Count - 1)) * (cw - 20) + 10 : 10;
                                    var y = ch - 10 - (samples[k] / maxSpeed) * (ch - 20);
                                    linePoints.Add(new Point(x, y));
                                    fillPoints.Add(new Point(x, y));
                                }
                                fillPoints.Add(new Point(cw - 10, ch - 10));
                                dlChartLine.Points = linePoints;
                                dlChartFill.Points = fillPoints;
                            }
                            lastTotalRead = totalRead;
                        }
                        sw.Stop();
                        var mbps = (totalRead * 8.0) / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
                        totalDlMbps += mbps;
                        dlCount++;
                    }
                    if (dlCount > 0 && !ct.IsCancellationRequested)
                    {
                        var avgDl = totalDlMbps / dlCount;
                        dlVal.Text = $"{avgDl:F1} Mbps";
                        dlVal.Foreground = avgDl > 100
                            ? new SolidColorBrush(WColor.FromArgb(255, 0, 180, 100))
                            : avgDl > 30
                                ? new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0))
                                : new SolidColorBrush(WColor.FromArgb(255, 220, 50, 50));
                    }
                }

                // 上传测试
                if (!ct.IsCancellationRequested)
                {
                    statusText.Text = "上传测试中...";
                    progressBar.Value = 80;
                    ulChartLine.Points.Clear();
                    ulChartFill.Points.Clear();
                    ulChartCanvas.Width = ulChartBorder.ActualWidth > 0 ? ulChartBorder.ActualWidth : 600;
                    ulChartCanvas.Height = ulChartBorder.ActualHeight > 0 ? ulChartBorder.ActualHeight : 150;
                    var ulSamples = new List<double>();
                    var chunkSize = 512 * 1024;
                    var totalUl = 10 * 1024 * 1024;
                    var sw = Stopwatch.StartNew();
                    long ulSent = 0;
                    while (ulSent < totalUl && !ct.IsCancellationRequested)
                    {
                        var thisChunk = (int)Math.Min(chunkSize, totalUl - ulSent);
                        var chunk = new byte[thisChunk];
                        var content = new ByteArrayContent(chunk);
                        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                        var uploadResp = await client.PostAsync($"{cfBase}/__up", content, ct);
                        if (!uploadResp.IsSuccessStatusCode) break;
                        ulSent += thisChunk;
                        var elapsed = sw.Elapsed.TotalSeconds;
                        if (elapsed > 0)
                        {
                            var ulMbps = (ulSent * 8.0) / (1024.0 * 1024.0) / elapsed;
                            ulSamples.Add(ulMbps);
                            DispatcherQueue.TryEnqueue(() => ulSpeedOverlay.Text = $"{ulMbps:F1} Mbps");
                            // 更新上传图表
                            var ucw = ulChartCanvas.Width;
                            var uch = ulChartCanvas.Height;
                            var ulMax = ulSamples.Max();
                            if (ulMax < 1) ulMax = 1;
                            var ulLinePts = new PointCollection();
                            var ulFillPts = new PointCollection();
                            ulFillPts.Add(new Point(10, uch - 10));
                            for (int k = 0; k < ulSamples.Count; k++)
                            {
                                var x = ulSamples.Count > 1 ? (k / (double)(ulSamples.Count - 1)) * (ucw - 20) + 10 : 10;
                                var y = uch - 10 - (ulSamples[k] / ulMax) * (uch - 20);
                                ulLinePts.Add(new Point(x, y));
                                ulFillPts.Add(new Point(x, y));
                            }
                            ulFillPts.Add(new Point(ucw - 10, uch - 10));
                            ulChartLine.Points = ulLinePts;
                            ulChartFill.Points = ulFillPts;
                        }
                    }
                    sw.Stop();
                    if (ulSamples.Count > 0 && !ct.IsCancellationRequested)
                    {
                        var avgUl = ulSamples.Average();
                        ulVal.Text = $"{avgUl:F1} Mbps";
                        ulVal.Foreground = avgUl > 50
                            ? new SolidColorBrush(WColor.FromArgb(255, 0, 180, 100))
                            : avgUl > 10
                                ? new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0))
                                : new SolidColorBrush(WColor.FromArgb(255, 220, 50, 50));
                    }
                }

                if (!ct.IsCancellationRequested)
                {
                    progressBar.Value = 100;
                    statusText.Text = "测试完成";
                }
            }
            catch (OperationCanceledException) { statusText.Text = "已中断"; }
            catch { statusText.Text = "测试失败"; }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                startBtn.IsEnabled = true;
                startBtn.Content = "⟳ 重新测试";
            }
        }

        startBtn.Click += async (s, e) =>
        {
            speedCts?.Cancel();
            speedCts = new CancellationTokenSource();
            await RunSpeedTest(speedCts.Token);
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

    private class PingCardData
    {
        public TextBlock AvgLabel { get; set; }
        public Ellipse[] Dots { get; set; }
    }

    private PingCardData[] AddPingCards(Grid grid, (string, string, string)[] targets)
    {
        var cards = new PingCardData[targets.Length];
        int cols = 4;
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (int i = 0; i < cols; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < targets.Length; i++)
        {
            if (i % cols == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var card = new Border
            {
                Background = _cardBgBrush,
                BorderBrush = _cardBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(4, 4, 4, 4),
                MinHeight = 90
            };
            var stack = new StackPanel();

            // 顶行：图标 + 名称 | 平均延迟
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition());
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
            namePanel.Children.Add(new TextBlock { Text = targets[i].Item1, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            topRow.Children.Add(namePanel);
            var avgLabel = new TextBlock { Text = "-- ms", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = _textPrimaryBrush, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(avgLabel, 1);
            topRow.Children.Add(avgLabel);
            stack.Children.Add(topRow);

            // 底行：10 次圆点
            var detailPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(0, 6, 0, 0) };
            var dots = new Ellipse[10];
            for (int j = 0; j < 10; j++)
            {
                dots[j] = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(WColor.FromArgb(255, 200, 200, 200)),
                    Margin = new Thickness(1, 0, 1, 0)
                };
                detailPanel.Children.Add(dots[j]);
            }
            stack.Children.Add(detailPanel);

            card.Child = stack;
            Grid.SetColumn(card, i % cols);
            Grid.SetRow(card, i / cols);
            grid.Children.Add(card);
            cards[i] = new PingCardData { AvgLabel = avgLabel, Dots = dots };
        }
        return cards;
    }

    private async Task RunPingTestsDetailed((string, string, string)[] targets, PingCardData[] cards, CancellationToken ct = default)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (ct.IsCancellationRequested) return;
            var valid = new List<int>();
            for (int j = 0; j < 10; j++)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    using var ping = new System.Net.NetworkInformation.Ping();
                    var reply = await ping.SendPingAsync(targets[i].Item2, 3000);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    {
                        var ms = (int)reply.RoundtripTime;
                        valid.Add(ms);
                        cards[i].Dots[j].Fill = ms < 100
                            ? new SolidColorBrush(WColor.FromArgb(255, 0, 180, 100))
                            : ms < 300
                                ? new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0))
                                : new SolidColorBrush(WColor.FromArgb(255, 220, 50, 50));
                    }
                    else
                    {
                        cards[i].Dots[j].Fill = new SolidColorBrush(WColor.FromArgb(255, 200, 50, 50));
                    }
                }
                catch
                {
                    cards[i].Dots[j].Fill = new SolidColorBrush(WColor.FromArgb(255, 200, 50, 50));
                }
            }
            if (valid.Count > 0)
            {
                var avg = (int)valid.Average();
                cards[i].AvgLabel.Text = $"{avg} ms";
                cards[i].AvgLabel.Foreground = avg < 100
                    ? new SolidColorBrush(WColor.FromArgb(255, 0, 180, 100))
                    : avg < 300
                        ? new SolidColorBrush(WColor.FromArgb(255, 255, 140, 0))
                        : new SolidColorBrush(WColor.FromArgb(255, 220, 50, 50));
            }
            else
            {
                cards[i].AvgLabel.Text = "超时";
                cards[i].AvgLabel.Foreground = new SolidColorBrush(WColor.FromArgb(255, 200, 50, 50));
            }
        }
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
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidate1 = Path.Combine(baseDir, iconPath);
                var candidate2 = Path.Combine(baseDir, "..", "..", "..", "..", "plugin", "icon", Path.GetFileName(iconPath));
                iconPath = File.Exists(candidate1) ? candidate1 : (File.Exists(candidate2) ? candidate2 : candidate1);
            }
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
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

        var isStoreDl = tool.Category == "工具商店" && tool.Action.StartsWith("dl:");
        var pluginName = isStoreDl ? tool.Action.Substring(3) : null;
        var isInstalled = isStoreDl && PluginConfigs.TryGetValue(pluginName!, out var cfg)
            && File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, cfg.checkFile));
        var openBtn = new Button
        {
            Content = isStoreDl ? (isInstalled ? "打开" : "安装") : "打开",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6),
            Background = new SolidColorBrush(isStoreDl && !isInstalled ? WColor.FromArgb(255, 0, 150, 0) : WColor.FromArgb(255, 0, 120, 212)),
            Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Tag = tool
        };
        openBtn.Click += (s, e) =>
        {
            if (isInstalled)
            {
                if (pluginName == "AcceleratorHelper")
                {
                    if (_navAcceleratorItem != null)
                    {
                        navView.SelectedItem = _navAcceleratorItem;
                        LoadContent("accelerator");
                    }
                    else
                    {
                        ShowAcceleratorPage();
                    }
                }
                else
                {
                    if (_navProxyItem != null)
                    {
                        navView.SelectedItem = _navProxyItem;
                        LoadContent("proxy");
                    }
                    else
                    {
                        ShowProxyTools();
                    }
                }
            }
            else
                ExecuteToolAction(tool.Action);
        };
        Grid.SetColumn(openBtn, 2);

        if (isStoreDl && isInstalled)
        {
            var uninstallBtn = new Button
            {
                Content = "卸载",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(20, 6, 20, 6),
                Background = new SolidColorBrush(WColor.FromArgb(255, 232, 17, 35)),
                Foreground = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                Tag = tool
            };
            uninstallBtn.Click += async (s, e) =>
            {
                if (pluginName == null || !PluginConfigs.TryGetValue(pluginName, out var ucfg)) return;
                var targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    string.IsNullOrEmpty(ucfg.extractFolder) ? pluginName : ucfg.extractFolder);
                try
                {
                    if (Directory.Exists(targetDir))
                        Directory.Delete(targetDir, true);
                    UpdateProxyNavItem();
                    UpdateAcceleratorNavItem();
                    ShowMessageDialog($"{ucfg.displayName} 已卸载。");
                }
                catch (Exception ex)
                {
                    ShowMessageDialog($"卸载失败: {ex.Message}");
                }
                LoadContent(GetCurrentTag());
            };
            Grid.SetColumn(uninstallBtn, 1);
            bottomRow.Children.Add(uninstallBtn);
        }

        bottomRow.Children.Add(settingsBtn);
        bottomRow.Children.Add(openBtn);
        Grid.SetRow(bottomRow, 3);

        root.Children.Add(headerRow);
        root.Children.Add(descBlock);
        root.Children.Add(bottomRow);

        card.Child = root;
        return card;
    }

    private static bool IsProxyToolsInstalled()
    {
        return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProxyTools", "mihomo", "mihomo.exe"));
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
            else if (action.StartsWith("dl:"))
                _ = DownloadAndExtractPlugin(action.Substring(3));
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

    private bool _isDownloading;

    private static readonly Dictionary<string, (string checkFile, string extractFolder, string displayName, string installMsg)> PluginConfigs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProxyTools"] = (Path.Combine("ProxyTools", "mihomo", "mihomo.exe"), "", "ProxyTools", "ProxyTools 安装成功！左侧导航栏将出现「代理工具」。"),
        ["AcceleratorHelper"] = (Path.Combine("AcceleratorHelper", "AcceleratorHelper.exe"), "AcceleratorHelper", "网络加速器", "网络加速器安装成功！可在「网络加速」页面启动使用。"),
    };

    private async Task DownloadAndExtractPlugin(string pluginName)
    {
        if (_isDownloading) return;
        if (!PluginConfigs.TryGetValue(pluginName, out var config))
        {
            ShowMessageDialog($"未知插件: {pluginName}");
            return;
        }
        var (checkFile, extractFolder, displayName, installMsg) = config;

        _isDownloading = true;
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(baseDir, checkFile)))
            {
                ShowMessageDialog($"{displayName} 已安装！如需重新安装，请先删除相关文件夹。");
                return;
            }

            downloadStatusText.Text = "正在连接...";
            downloadProgressBar.IsIndeterminate = true;
            downloadFooter.Visibility = Visibility.Visible;

            var rawUrl = $"https://github.com/bnganblog/XKBTOOL/raw/master/plugin/{pluginName}.zip";
            var proxyPrefix = App.DownloadProxy;
            var urls = string.IsNullOrEmpty(proxyPrefix)
                ? new[] { rawUrl }
                : new[] { proxyPrefix + rawUrl, rawUrl };
            var tempDir = Path.Combine(Path.GetTempPath(), "XKBToolbox_Plugin");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, $"{pluginName}.zip");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("XKBToolbox");
                client.Timeout = TimeSpan.FromMinutes(5);
                HttpResponseMessage? response = null;
                foreach (var url in urls)
                {
                    try
                    {
                        response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        break;
                    }
                    catch
                    {
                        response?.Dispose();
                        response = null;
                    }
                }
                if (response == null) throw new HttpRequestException("所有下载源均不可用");
                var total = response.Content.Headers.ContentLength ?? -1;
                downloadProgressBar.IsIndeterminate = false;
                downloadProgressBar.Maximum = total > 0 ? total : 100;
                using var fs = File.Create(zipPath);
                using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[8192];
                long read = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, count);
                    read += count;
                    if (total > 0)
                    {
                        var pct = (int)(read * 100 / total);
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            downloadProgressBar.Value = read;
                            downloadStatusText.Text = $"{FormatBytes(read)} / {FormatBytes(total)} ({pct}%)";
                        });
                    }
                    else
                    {
                        DispatcherQueue.TryEnqueue(() => downloadStatusText.Text = $"已下载 {FormatBytes(read)}");
                    }
                }
            }

            downloadStatusText.Text = "正在解压...";
            downloadProgressBar.IsIndeterminate = true;
            var extractDir = string.IsNullOrEmpty(extractFolder) ? baseDir : Path.Combine(baseDir, extractFolder);
            if (!string.IsNullOrEmpty(extractFolder))
                Directory.CreateDirectory(extractDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            try { File.Delete(zipPath); } catch { }

            downloadFooter.Visibility = Visibility.Collapsed;
            ShowMessageDialog(installMsg);

            if (pluginName == "ProxyTools")
            {
                UpdateProxyNavItem();
                if (_navProxyItem != null)
                {
                    navView.SelectedItem = _navProxyItem;
                    LoadContent("proxy");
                }
            }
            else if (pluginName == "AcceleratorHelper")
            {
                UpdateAcceleratorNavItem();
                if (_navAcceleratorItem != null)
                {
                    navView.SelectedItem = _navAcceleratorItem;
                    LoadContent("accelerator");
                }
            }
        }
        catch (HttpRequestException)
        {
            downloadFooter.Visibility = Visibility.Collapsed;
            ShowMessageDialog("安装失败：无法下载插件，请检查网络连接或稍后重试。");
        }
        catch (Exception ex)
        {
            downloadFooter.Visibility = Visibility.Collapsed;
            ShowMessageDialog($"安装失败: {ex.Message}");
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    internal async void ShowMessageDialog(string message, string title = "提示")
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
        if (_navHistory.Count > 0)
        {
            var prevTag = _navHistory.Pop();
            if (prevTag == _currentTag && _navHistory.Count > 0)
                prevTag = _navHistory.Pop();
            _suppressNavHistory = true;
            if (prevTag == "settings")
            {
                navView.SelectedItem = null;
            }
            else
            {
                foreach (var item in navView.MenuItems)
                {
                    if (item is NavigationViewItem nvi && nvi.Tag?.ToString() == prevTag)
                    { navView.SelectedItem = nvi; break; }
                }
            }
            LoadContent(prevTag);
            _suppressNavHistory = false;
        }
    }

    #endregion

    #region 设置对话框

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        PushNavHistory(_currentTag);
        _currentTag = "settings";
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

        bool barMode = App.SysInfoStyle == "Bar";
        object cpuChart, memChart, gpuChart;

        if (barMode)
        {
            var cpuBar = new HorizontalBarChart("CPU", WColor.FromArgb(255, 0, 120, 212));
            var memBar = new HorizontalBarChart("内存", WColor.FromArgb(255, 16, 137, 62));
            var gpuBar = new HorizontalBarChart("GPU", WColor.FromArgb(255, 196, 43, 28));
            var barStack = new StackPanel();
            barStack.Children.Add(cpuBar);
            barStack.Children.Add(memBar);
            barStack.Children.Add(gpuBar);
            chartCard.Child = barStack;
            cpuChart = cpuBar; memChart = memBar; gpuChart = gpuBar;
        }
        else
        {
            var cpuCirc = new UsageChart("CPU", WColor.FromArgb(255, 0, 120, 212));
            Grid.SetColumn(cpuCirc, 0);
            var memCirc = new UsageChart("内存", WColor.FromArgb(255, 16, 137, 62));
            Grid.SetColumn(memCirc, 1);
            var gpuCirc = new UsageChart("GPU", WColor.FromArgb(255, 196, 43, 28));
            Grid.SetColumn(gpuCirc, 2);
            chartGrid.Children.Add(cpuCirc);
            chartGrid.Children.Add(memCirc);
            chartGrid.Children.Add(gpuCirc);
            chartCard.Child = chartGrid;
            cpuChart = cpuCirc; memChart = memCirc; gpuChart = gpuCirc;
        }

        stackPanel.Children.Add(chartCard);

        stackPanel.Children.Add(CreateInfoCard("硬盘信息", new[] { CreateInfoRow("硬盘", _diskInfo), CreateInfoRow("型号", _diskModel) }));
        stackPanel.Children.Add(CreateInfoCard("系统信息", new[] { CreateInfoRow("操作系统", _osInfo), CreateInfoRow("计算机名", Environment.MachineName), CreateInfoRow("用户名", Environment.UserName), CreateInfoRow("本机IP", _localIP), CreateInfoRow("声卡", _audioInfo), CreateInfoRow("显示器", _monitorInfo) }));

        _systemInfoCache = stackPanel;
        contentArea.Children.Add(stackPanel);
        StartChartTimer(cpuChart, memChart, gpuChart);
    }

    private void StartChartTimer(object cpuChart, object memChart, object gpuChart)
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

        static void AddVal(object chart, float val)
        {
            if (chart is UsageChart uc) uc.AddValue(val);
            else if (chart is HorizontalBarChart hb) hb.AddValue(val);
        }

        _chartTimer = DispatcherQueue.CreateTimer();
        _chartTimer.Interval = TimeSpan.FromSeconds(1);
        _chartTimer.Tick += (s, e) =>
        {
            try
            {
                AddVal(cpuChart, _cpuCounter?.NextValue() ?? 0);
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
                AddVal(memChart, memVal);

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
                AddVal(gpuChart, gpuVal);
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
            _appHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _wndProcDelegate = WndProc;
            _oldWndProc = SetWindowLongPtr(_appHwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            CreateTrayIcon();
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCLBUTTONDBLCLK && (int)wParam == HTCAPTION)
            return IntPtr.Zero;
        if (msg == WM_TRAYICON)
        {
            var lw = (int)lParam;
            if (lw == 0x0203) // WM_LBUTTONDBLCLK
            {
                ShowFromTray();
            }
            else if (lw == 0x0205) // WM_RBUTTONUP
            {
                ShowTrayMenu();
            }
        }
        if (msg == WM_CLOSE && _trayIconAdded)
        {
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                var dlg = new ContentDialog
                {
                    Title = "退出确认",
                    Content = "是否最小化到系统托盘？",
                    PrimaryButtonText = "最小化到托盘",
                    SecondaryButtonText = "退出程序",
                    CloseButtonText = "取消",
                    XamlRoot = contentArea.XamlRoot
                };
                var result = await dlg.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    MinimizeToTray();
                else if (result == ContentDialogResult.Secondary)
                {
                    _trayIconAdded = false;
                    DestroyTrayIcon();
                    Application.Current.Exit();
                }
            });
            return IntPtr.Zero;
        }
        if (msg == 0x0111) // WM_COMMAND
        {
            var cmd = (uint)wParam & 0xFFFF;
            if (cmd == TRAY_SHOW) ShowFromTray();
            else if (cmd == TRAY_EXIT)
            {
                _trayIconAdded = false;
                DestroyTrayIcon();
                Application.Current.Exit();
            }
        }
        try { return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam); }
        catch { return IntPtr.Zero; }
    }

    private void CreateTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", "app.ico");
            IntPtr hIcon;
            if (File.Exists(iconPath))
                hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            else
                hIcon = LoadImage(IntPtr.Zero, "app.ico", IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
            if (hIcon == IntPtr.Zero)
                hIcon = LoadImage(IntPtr.Zero, "ToolboxWinUI.exe", IMAGE_ICON, 32, 32, 0);

            _trayData = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _appHwnd,
                uID = 0,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = hIcon,
                szTip = "显卡吧工具箱WinUI3"
            };
            _trayIconAdded = Shell_NotifyIcon(NIM_ADD, ref _trayData);
        }
        catch { }
    }

    private void DestroyTrayIcon()
    {
        if (_trayIconAdded)
        {
            _trayData.uFlags = 0;
            Shell_NotifyIcon(NIM_DELETE, ref _trayData);
            _trayIconAdded = false;
        }
    }

    private void MinimizeToTray()
    {
        ShowWindow(_appHwnd, SW_HIDE);
    }

    private void ShowFromTray()
    {
        ShowWindow(_appHwnd, SW_SHOW);
        SetForegroundWindow(_appHwnd);
    }

    private void ShowTrayMenu()
    {
        try
        {
            var hMenu = CreatePopupMenu();
            InsertMenu(hMenu, 0, MF_STRING, TRAY_SHOW, "显示主窗口");
            InsertMenu(hMenu, 1, MF_SEPARATOR, 0, null);
            InsertMenu(hMenu, 2, MF_STRING, TRAY_EXIT, "退出");

            if (!GetCursorPos(out var pt)) return;

            SetForegroundWindow(_appHwnd);
            TrackPopupMenu(hMenu, TPM_LEFTALIGN | TPM_BOTTOMALIGN, pt.X, pt.Y, 0, _appHwnd, IntPtr.Zero);
            DestroyMenu(hMenu);
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    internal List<ToolInfo> GetTools() => _allTools;
    internal void SetTools(List<ToolInfo> tools) { _allTools = tools; SaveTools(); }
    internal void ResetTools() { InitDefaultTools(); }
    internal void InvalidateSysInfoCache() { _systemInfoCache = null; }
    internal void RefreshCurrentPage() { LoadContent(GetCurrentTag()); }
    internal XamlRoot GetContentXamlRoot() => contentArea.XamlRoot;

    #endregion
}
