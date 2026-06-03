using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WColor = Windows.UI.Color;

namespace ToolboxWinUI.ProxyTools;

public class ProxyPage : Grid
{
    private readonly ProxyEngine _engine = new();
    private readonly DashboardServer _dashboardServer = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly Ellipse _statusDot;
    private readonly TextBlock _statusText;
    private readonly Button _toggleBtn;
    private readonly ComboBox _modeCombo;
    private readonly Button _refreshBtn;
    private readonly StackPanel _groupsPanel;
    private readonly ScrollViewer _groupsScroll;
    private readonly TextBlock _logText;
    private readonly ScrollViewer _logScroll;
    private readonly TextBox _configText;
    private readonly ScrollViewer _configScroll;
    private Button _proxiesTab, _logTab, _configTab, _apiBtn;
    private bool _updating;

    public ProxyPage()
    {
        Margin = new Thickness(0, 0, 0, 24);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        #region Toolbar
        _statusDot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(WColor.FromArgb(255, 128, 128, 128)), VerticalAlignment = VerticalAlignment.Center };
        _statusText = new TextBlock { Text = "未启动", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        _toggleBtn = new Button { Content = "启动", Padding = new Thickness(16, 6, 16, 6), MinWidth = 80 };
        _modeCombo = new ComboBox { MinWidth = 100, SelectedIndex = 0, IsEnabled = false };
        _modeCombo.Items.Add("Rule");
        _modeCombo.Items.Add("Global");
        _modeCombo.Items.Add("Direct");
        _refreshBtn = new Button { Content = "\uE72C", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Padding = new Thickness(12, 6, 12, 6), MinWidth = 44 };

        var toolbar = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8) };
        var tg = new Grid();
        for (int i = 0; i < 7; i++)
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tg.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);

        var sep = new TextBlock { Text = "|", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Opacity = 0.3 };
        var modeLabel = new TextBlock { Text = "模式:", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

        _statusDot.Margin = new Thickness(0, 0, 8, 0);
        _statusText.Margin = new Thickness(0, 0, 12, 0);
        _toggleBtn.Margin = new Thickness(0, 0, 12, 0);

        Grid.SetColumn(_statusDot, 0);
        Grid.SetColumn(_statusText, 1);
        Grid.SetColumn(sep, 2);
        Grid.SetColumn(_toggleBtn, 3);
        Grid.SetColumn(modeLabel, 4);
        Grid.SetColumn(_modeCombo, 5);
        Grid.SetColumn(_refreshBtn, 6);

        tg.Children.Add(_statusDot);
        tg.Children.Add(_statusText);
        tg.Children.Add(sep);
        tg.Children.Add(_toggleBtn);
        tg.Children.Add(modeLabel);
        tg.Children.Add(_modeCombo);
        tg.Children.Add(_refreshBtn);
        toolbar.Child = tg;
        Children.Add(toolbar);
        Grid.SetRow(toolbar, 0);
        #endregion

        #region Content area with 3 tabs
        _groupsPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        _groupsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _groupsPanel };
        _logText = new TextBlock { FontSize = 12, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap };
        _logScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _logText, Margin = new Thickness(8) };
        _configText = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 8, 8, 8),
            MinHeight = 200,
            MaxHeight = 600
        };
        var configToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
        var uploadBtn = new Button { Content = "上传配置", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var saveBtn = new Button { Content = "保存配置", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var restartBtn = new Button { Content = "保存并重启", Padding = new Thickness(12, 4, 12, 4) };
        uploadBtn.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads
                };
                picker.FileTypeFilter.Add(".yaml");
                picker.FileTypeFilter.Add(".yml");
                var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var content = await Windows.Storage.FileIO.ReadTextAsync(file);
                    _configText.Text = content;
                    ShowToast($"已加载: {file.Name}");
                }
            }
            catch (Exception ex) { ShowToast($"上传失败: {ex.Message}"); }
        };
        saveBtn.Click += async (_, _) => { await _engine.SaveConfigAsync(_configText.Text); ShowToast("配置已保存"); };
        restartBtn.Click += async (_, _) =>
        {
            await _engine.SaveConfigAsync(_configText.Text);
            if (_engine.IsRunning)
                await _engine.RestartAsync();
            else
                await _engine.StartAsync();
            ShowToast("已应用并重启");
        };
        configToolbar.Children.Add(uploadBtn);
        configToolbar.Children.Add(saveBtn);
        configToolbar.Children.Add(restartBtn);
        var configStack = new StackPanel();
        configStack.Children.Add(configToolbar);
        configStack.Children.Add(_configText);
        _configScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = configStack };

        var tabBar = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        tabBar.ColumnDefinitions.Add(new ColumnDefinition());
        tabBar.ColumnDefinitions.Add(new ColumnDefinition());
        tabBar.ColumnDefinitions.Add(new ColumnDefinition());
        _proxiesTab = new Button { Content = "代理节点", FontSize = 14, Background = new SolidColorBrush(WColor.FromArgb(20, 0, 120, 212)), BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
        _logTab = new Button { Content = "运行日志", FontSize = 14, BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
        _configTab = new Button { Content = "配置文件", FontSize = 14, BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(_proxiesTab, 0);
        Grid.SetColumn(_logTab, 1);
        Grid.SetColumn(_configTab, 2);
        tabBar.Children.Add(_proxiesTab);
        tabBar.Children.Add(_logTab);
        tabBar.Children.Add(_configTab);

        _proxiesTab.Click += (_, _) => SwitchTab("proxies");
        _logTab.Click += (_, _) => { _logText.Text = GetLogText(); SwitchTab("log"); };
        _configTab.Click += (_, _) => { _configText.Text = _engine.GetConfig(); SwitchTab("config"); };

        var tabContent = new Grid();
        tabContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tabContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tabContent.Children.Add(tabBar);
        Grid.SetRow(tabBar, 0);
        tabContent.Children.Add(_groupsScroll);
        Grid.SetRow(_groupsScroll, 1);
        tabContent.Children.Add(_logScroll);
        Grid.SetRow(_logScroll, 1);
        _logScroll.Visibility = Visibility.Collapsed;
        tabContent.Children.Add(_configScroll);
        Grid.SetRow(_configScroll, 1);
        _configScroll.Visibility = Visibility.Collapsed;

        Children.Add(tabContent);
        Grid.SetRow(tabContent, 1);
        #endregion

        #region Status bar
        var statusBar = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 8, 0, 0) };
        var statusRow = new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition());
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var portText = new TextBlock { Text = "HTTP: 7890 | SOCKS: 7891 | 面板: 127.0.0.1:19090", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _apiBtn = new Button { Content = "打开面板", FontSize = 12, Padding = new Thickness(12, 4, 12, 4), MinWidth = 0, IsEnabled = false };
        _apiBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("http://127.0.0.1:19090") { UseShellExecute = true });
        Grid.SetColumn(portText, 0);
        Grid.SetColumn(_apiBtn, 1);
        statusRow.Children.Add(portText);
        statusRow.Children.Add(_apiBtn);
        statusBar.Child = statusRow;
        Children.Add(statusBar);
        Grid.SetRow(statusBar, 2);
        #endregion

        #region Events
        _toggleBtn.Click += async (_, _) =>
        {
            if (_engine.IsRunning)
            {
                _dashboardServer.Stop();
                _engine.Stop();
                await UpdateStatusAsync();
            }
            else
            {
                _toggleBtn.IsEnabled = false;
                _toggleBtn.Content = "启动中...";
                if (ProxyEngine.IsKernelRunning)
                {
                    _statusText.Text = "正在关闭外部内核...";
                    _engine.KillExternalKernels();
                    await Task.Delay(500);
                }
                await _engine.StartAsync();
                _dashboardServer.Start();
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(1000);
                    if (await IsApiRunningAsync())
                    {
                        await UpdateStatusAsync();
                        await LoadProxiesAsync();
                        _toggleBtn.IsEnabled = true;
                        return;
                    }
                }
                var kernelStatus = _engine.GetKernelStatus();
                _statusText.Text = $"启动超时 - {kernelStatus}";
                _statusDot.Fill = new SolidColorBrush(WColor.FromArgb(255, 200, 0, 0));
                _toggleBtn.IsEnabled = true;
                _toggleBtn.Content = "启动";
            }
        };
        _refreshBtn.Click += async (_, _) =>
        {
            await UpdateStatusAsync();
            await LoadProxiesAsync();
        };
        _modeCombo.SelectionChanged += async (_, _) =>
        {
            if (_updating || !await IsApiRunningAsync()) return;
            if (_modeCombo.SelectedIndex >= 0)
                await SetModeAsync(new[] { "Rule", "Global", "Direct" }[_modeCombo.SelectedIndex]);
        };
        _engine.StatusChanged += () => _ = DispatcherQueue.TryEnqueue(async () => await UpdateStatusAsync());
        Loaded += async (_, _) => await UpdateStatusAsync();
        #endregion
    }

    private void SwitchTab(string tab)
    {
        var showProxies = tab == "proxies";
        var showLog = tab == "log";
        var showConfig = tab == "config";
        _groupsPanel.Visibility = showProxies ? Visibility.Visible : Visibility.Collapsed;
        _groupsScroll.Visibility = showProxies ? Visibility.Visible : Visibility.Collapsed;
        _logScroll.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
        _configScroll.Visibility = showConfig ? Visibility.Visible : Visibility.Collapsed;
        var activeBrush = new SolidColorBrush(WColor.FromArgb(20, 0, 120, 212));
        var inactiveBrush = new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0));
        _proxiesTab.Background = showProxies ? activeBrush : inactiveBrush;
        _logTab.Background = showLog ? activeBrush : inactiveBrush;
        _configTab.Background = showConfig ? activeBrush : inactiveBrush;
    }

    private async Task UpdateStatusAsync()
    {
        var running = await IsApiRunningAsync();
        if (!running) _dashboardServer.Stop();
        var kernelStatus = _engine.GetKernelStatus();
        if (running)
        {
            _statusDot.Fill = new SolidColorBrush(WColor.FromArgb(255, 0, 200, 83));
            _statusText.Text = _engine.IsRunning ? "运行中" : "外部内核运行中";
        }
        else if (_engine.IsRunning)
        {
            _statusDot.Fill = new SolidColorBrush(WColor.FromArgb(255, 255, 165, 0));
            _statusText.Text = "启动中...";
        }
        else if (ProxyEngine.IsKernelRunning)
        {
            _statusDot.Fill = new SolidColorBrush(WColor.FromArgb(255, 200, 130, 0));
            _statusText.Text = "外部内核运行中";
        }
        else
        {
            _statusDot.Fill = new SolidColorBrush(WColor.FromArgb(255, 200, 0, 0));
            _statusText.Text = kernelStatus;
        }
        _toggleBtn.Content = _engine.IsRunning ? "停止" : "启动";
        _toggleBtn.IsEnabled = true;
        _modeCombo.IsEnabled = running;
        _refreshBtn.IsEnabled = running;
        _apiBtn.IsEnabled = running;
        if (running)
        {
            _dashboardServer.Start();
            _updating = true;
            var mode = await GetModeAsync();
            _modeCombo.SelectedIndex = mode switch { "Global" => 1, "Direct" => 2, _ => 0 };
            _updating = false;
            await LoadProxiesAsync();
        }
    }

    private async Task LoadProxiesAsync()
    {
        if (!await IsApiRunningAsync()) return;
        _groupsPanel.Children.Clear();
        var groups = await GetProxiesAsync();
        if (groups.Count == 0)
        {
            _groupsPanel.Children.Add(new TextBlock
            {
                Text = "无代理策略组，请在「配置文件」标签中添加 proxies 配置后重启",
                FontSize = 14,
                Foreground = new SolidColorBrush(WColor.FromArgb(255, 156, 156, 156)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 24, 16, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }
        foreach (var (name, all, now, _) in groups)
            _groupsPanel.Children.Add(CreateGroupSection(name, all, now));
    }

    private UIElement CreateGroupSection(string name, List<string> all, string now)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(WColor.FromArgb(60, 150, 150, 150)),
            Background = new SolidColorBrush(WColor.FromArgb(12, 0, 0, 0)),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"{name}  (当前: {now})",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var proxyName in all)
            stack.Children.Add(CreateProxyCard(name, proxyName, proxyName == now));
        border.Child = stack;
        return border;
    }

    private Border CreateProxyCard(string groupName, string proxyName, bool isSelected)
    {
        var card = new Border
        {
            Background = isSelected ? new SolidColorBrush(WColor.FromArgb(25, 0, 120, 212)) : new SolidColorBrush(WColor.FromArgb(0, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Ellipse
        {
            Width = 10, Height = 10,
            Fill = new SolidColorBrush(isSelected ? WColor.FromArgb(255, 0, 120, 212) : WColor.FromArgb(100, 150, 150, 150)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        Grid.SetColumn((FrameworkElement)row.Children[^1], 0);
        row.Children.Add(new TextBlock { Text = proxyName, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn((FrameworkElement)row.Children[^1], 1);
        card.Child = row;
        card.PointerPressed += async (_, _) =>
        {
            await SelectProxyAsync(groupName, proxyName);
            await LoadProxiesAsync();
        };
        return card;
    }

    private string GetLogText()
    {
        try
        {
            var logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToolboxWinUI", "proxy", "logs");
            if (!Directory.Exists(logDir)) return "暂无日志";
            var logFile = Directory.GetFiles(logDir, "*.log").OrderByDescending(f => f).FirstOrDefault();
            if (logFile == null) return "暂无日志";
            return string.Join("\n", File.ReadLines(logFile).TakeLast(100));
        }
        catch { return "暂无日志"; }
    }

    private async void ShowToast(string msg)
    {
        _statusText.Text = msg;
        await Task.Delay(2000);
        if (_engine.IsRunning)
            _statusText.Text = await IsApiRunningAsync() ? "运行中" : "启动中...";
        else
            _statusText.Text = "未启动";
    }

    #region API calls
    private async Task<bool> IsApiRunningAsync()
    {
        try { _http.BaseAddress = new Uri(_engine.ApiBaseUrl); var r = await _http.GetAsync("/version"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    private async Task<List<(string Name, List<string> All, string Now, string Type)>> GetProxiesAsync()
    {
        try
        {
            _http.BaseAddress = new Uri(_engine.ApiBaseUrl);
            var resp = await _http.GetFromJsonAsync<JsonElement>("/proxies");
            if (!resp.TryGetProperty("proxies", out var proxies)) return [];
            var result = new List<(string, List<string>, string, string)>();
            foreach (var prop in proxies.EnumerateObject())
            {
                var g = prop.Value;
                if (!g.TryGetProperty("type", out var t)) continue;
                var type = t.GetString() ?? "";
                if (type is not ("Selector" or "URLTest" or "Fallback" or "LoadBalance")) continue;
                var all = new List<string>();
                if (g.TryGetProperty("all", out var a))
                    all = a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                result.Add((prop.Name, all, g.TryGetProperty("now", out var n) ? n.GetString() ?? "" : "", type));
            }
            return result;
        }
        catch { return []; }
    }

    private async Task<bool> SelectProxyAsync(string group, string proxyName)
    {
        try
        {
            _http.BaseAddress = new Uri(_engine.ApiBaseUrl);
            var body = new StringContent(JsonSerializer.Serialize(new { name = proxyName }), Encoding.UTF8, "application/json");
            return (await _http.PutAsync($"/proxies/{Uri.EscapeDataString(group)}", body)).IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<string?> GetModeAsync()
    {
        try { _http.BaseAddress = new Uri(_engine.ApiBaseUrl); var r = await _http.GetFromJsonAsync<JsonElement>("/configs"); return r.TryGetProperty("mode", out var m) ? m.GetString() : null; }
        catch { return null; }
    }

    private async Task<bool> SetModeAsync(string mode)
    {
        try { _http.BaseAddress = new Uri(_engine.ApiBaseUrl); var body = new StringContent(JsonSerializer.Serialize(new { mode }), Encoding.UTF8, "application/json"); return (await _http.PatchAsync("/configs", body)).IsSuccessStatusCode; }
        catch { return false; }
    }
    #endregion
}
