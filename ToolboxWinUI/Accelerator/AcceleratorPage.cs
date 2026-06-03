using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ToolboxWinUI.Accelerator;

public class AcceleratorPage : Grid
{
    private readonly MainWindow _mainWindow;
    private readonly HttpClient _httpClient;
    private static Process? _helperProcess;
    private DispatcherTimer? _statusTimer;
    private static bool _isRunning;

    private CheckBox _cbGitHub = null!;
    private CheckBox _cbSteam = null!;
    private CheckBox _cbSpotify = null!;
    private CheckBox _cbCloudflare = null!;
    private CheckBox _cbCopilot = null!;
    private TextBlock _latencyGitHub = null!;
    private TextBlock _latencySteam = null!;
    private TextBlock _latencySpotify = null!;
    private TextBlock _latencyCloudflare = null!;
    private TextBlock _latencyCopilot = null!;
    private Button _startBtn = null!;
    private Button _stopBtn = null!;
    private Button _testBtn = null!;
    private TextBlock _statusText = null!;
    private TextBlock _logText = null!;

    private static readonly Dictionary<string, string> ServiceUrls = new()
    {
        ["GitHub"] = "github.com",
        ["Steam"] = "store.steampowered.com",
        ["Spotify"] = "open.spotify.com",
        ["Cloudflare"] = "cloudflare.com",
        ["Copilot"] = "githubcopilot.com"
    };

    private readonly SolidColorBrush _accentBrush = new(Color.FromArgb(255, 0, 120, 212));
    private readonly SolidColorBrush _greenBrush = new(Color.FromArgb(255, 0, 165, 0));
    private readonly SolidColorBrush _redBrush = new(Color.FromArgb(255, 232, 17, 35));
    private SolidColorBrush _textPrimaryBrush = new(Color.FromArgb(255, 30, 30, 30));
    private SolidColorBrush _textSecondaryBrush = new(Color.FromArgb(255, 100, 100, 100));
    private SolidColorBrush _cardBgBrush = new(Color.FromArgb(255, 255, 255, 255));
    private SolidColorBrush _cardBorderBrush = new(Color.FromArgb(255, 220, 220, 220));

    private static readonly string HelperPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "AcceleratorHelper", "AcceleratorHelper.exe");

    public AcceleratorPage(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        Margin = new Thickness(24);
        ApplyTheme();
        BuildUI();
        if (_isRunning)
        {
            _startBtn.IsEnabled = false;
            _stopBtn.IsEnabled = true;
            _statusText.Text = "运行中 ●";
            _statusText.Foreground = new SolidColorBrush(Colors.Green);
        }
        App.ThemeChanged += () => DispatcherQueue.TryEnqueue(() =>
        {
            ApplyTheme();
            Children.Clear();
            BuildUI();
            if (_isRunning)
            {
                _startBtn.IsEnabled = false;
                _stopBtn.IsEnabled = true;
            }
            _statusTimer?.Start();
        });
        Unloaded += (_, _) =>
        {
            _statusTimer?.Stop();
        };
    }

    private void BuildUI()
    {
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(new TextBlock
        {
            Text = "网络加速",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = _textPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // 服务选择
        var servicePanel = CreateCard();
        servicePanel.Children.Add(new TextBlock
        {
            Text = "服务选择",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _cbGitHub = CreateServiceRow(servicePanel, "GitHub");
        _cbSteam = CreateServiceRow(servicePanel, "Steam");
        _cbSpotify = CreateServiceRow(servicePanel, "Spotify");
        _cbCloudflare = CreateServiceRow(servicePanel, "Cloudflare");
        _cbCopilot = CreateServiceRow(servicePanel, "Copilot");

        stack.Children.Add(servicePanel);

        // 控制
        var controlPanel = CreateCard();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _testBtn = CreateButton("测试连通性", _accentBrush, Colors.White);
        _testBtn.Click += TestBtn_Click;
        _startBtn = CreateButton("启动加速", _greenBrush, Colors.White);
        _startBtn.Click += StartBtn_Click;
        _stopBtn = CreateButton("停止加速", _redBrush, Colors.White);
        _stopBtn.Click += StopBtn_Click;
        _stopBtn.IsEnabled = false;
        btns.Children.Add(_testBtn);
        btns.Children.Add(_startBtn);
        btns.Children.Add(_stopBtn);
        controlPanel.Children.Add(btns);
        stack.Children.Add(controlPanel);

        // 状态
        var statusPanel = CreateCard();
        statusPanel.Children.Add(new TextBlock
        {
            Text = "状态",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _statusText = new TextBlock
        {
            Text = "未运行",
            FontSize = 14,
            Foreground = _textSecondaryBrush
        };
        statusPanel.Children.Add(_statusText);
        stack.Children.Add(statusPanel);

        // 日志
        var logPanel = CreateCard();
        logPanel.Children.Add(new TextBlock
        {
            Text = "日志",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = _textPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _logText = new TextBlock
        {
            Text = "等待启动...",
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Foreground = _textSecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 200
        };
        logPanel.Children.Add(_logText);
        stack.Children.Add(logPanel);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack
        };
        Children.Add(scrollViewer);

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += async (_, _) => await UpdateStatusAsync();
        _statusTimer.Start();
    }

    private StackPanel CreateCard()
    {
        return new StackPanel
        {
            Spacing = 4,
            Background = _cardBgBrush,
            BorderBrush = _cardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };
    }

    private Button CreateButton(string content, SolidColorBrush background,
        Windows.UI.Color foreground, double width = 0)
    {
        var btn = new Button
        {
            Content = content,
            Background = background,
            Foreground = new SolidColorBrush(foreground),
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(6),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        };
        if (width > 0) btn.Width = width;
        return btn;
    }

    private void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(HelperPath))
        {
            ShowMessage("未找到 AcceleratorHelper.exe，请先从工具商店下载安装网络加速器。");
            return;
        }

        var services = new List<string>();
        if (_cbGitHub.IsChecked == true) services.Add("GitHub");
        if (_cbSteam.IsChecked == true) services.Add("Steam");
        if (_cbSpotify.IsChecked == true) services.Add("Spotify");
        if (_cbCloudflare.IsChecked == true) services.Add("Cloudflare");
        if (_cbCopilot.IsChecked == true) services.Add("Copilot");

        if (services.Count == 0)
        {
            ShowMessage("请至少选择一个服务。");
            return;
        }

        try
        {
            var args = $"--services {string.Join(",", services)} --status-port 20800";
            _helperProcess = Process.Start(new ProcessStartInfo
            {
                FileName = HelperPath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            });

            _isRunning = true;
            _startBtn.IsEnabled = false;
            _stopBtn.IsEnabled = true;
            _statusText.Text = "启动中...";
            _statusText.Foreground = _accentBrush;
        }
        catch (Exception ex)
        {
            ShowMessage($"启动失败: {ex.Message}");
        }
    }

    private async void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _httpClient.PostAsync("http://127.0.0.1:20800/stop", null);
        }
        catch { }

        _isRunning = false;
        _startBtn.IsEnabled = true;
        _stopBtn.IsEnabled = false;
        _statusText.Text = "已停止";
        _statusText.Foreground = _textSecondaryBrush;
    }

    private async Task UpdateStatusAsync()
    {
        if (!_isRunning) return;
        try
        {
            var json = await _httpClient.GetStringAsync("http://127.0.0.1:20800/status");
            _statusText.Text = "运行中 ●";
            _statusText.Foreground = new SolidColorBrush(Colors.Green);

            var logsJson = await _httpClient.GetStringAsync("http://127.0.0.1:20800/logs");
            var lines = JsonSerializer.Deserialize<List<string>>(logsJson);
            if (lines != null && lines.Count > 0)
            {
                _logText.Text = string.Join("\n", lines.TakeLast(15));
            }
        }
        catch
        {
            if (_isRunning)
            {
                _statusText.Text = "运行中 ●";
                _statusText.Foreground = new SolidColorBrush(Colors.Green);
            }
        }
    }

    private void ApplyTheme()
    {
        bool glass = App.CurrentTheme == "DarkGlass" || App.CurrentTheme == "LightGlass";
        bool darkGlass = App.CurrentTheme == "DarkGlass";
        bool dark = App.CurrentTheme == "Dark";

        if (darkGlass || dark)
        {
            _textPrimaryBrush = new SolidColorBrush(Color.FromArgb(255, 224, 224, 224));
            _textSecondaryBrush = new SolidColorBrush(Color.FromArgb(255, 156, 156, 156));
            _cardBgBrush = glass
                ? new SolidColorBrush(Color.FromArgb(160, 30, 30, 30))
                : new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
            _cardBorderBrush = glass
                ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
        }
        else
        {
            _textPrimaryBrush = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
            _textSecondaryBrush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));
            _cardBgBrush = glass
                ? new SolidColorBrush(Color.FromArgb(100, 248, 248, 248))
                : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
            _cardBorderBrush = glass
                ? new SolidColorBrush(Color.FromArgb(60, 150, 150, 150))
                : new SolidColorBrush(Color.FromArgb(255, 220, 220, 220));
        }
    }

    private CheckBox CreateServiceRow(StackPanel parent, string name)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox { Content = name, IsChecked = true };
        row.Children.Add(cb);

        var latency = new TextBlock
        {
            Text = "-- ms",
            FontSize = 12,
            Foreground = _textSecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(latency, 2);
        row.Children.Add(latency);

        switch (name)
        {
            case "GitHub": _latencyGitHub = latency; break;
            case "Steam": _latencySteam = latency; break;
            case "Spotify": _latencySpotify = latency; break;
            case "Cloudflare": _latencyCloudflare = latency; break;
            case "Copilot": _latencyCopilot = latency; break;
        }

        parent.Children.Add(row);
        return cb;
    }

    private async void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        _testBtn.IsEnabled = false;
        await TestAllConnectivityAsync();
        _testBtn.IsEnabled = true;
    }

    private async Task TestAllConnectivityAsync()
    {
        var tasks = new List<Task>();
        if (_cbGitHub.IsChecked == true)
            tasks.Add(TestConnectivityAsync("GitHub", "github.com", _latencyGitHub));
        else
            _latencyGitHub.Text = "-- ms";
        if (_cbSteam.IsChecked == true)
            tasks.Add(TestConnectivityAsync("Steam", "store.steampowered.com", _latencySteam));
        else
            _latencySteam.Text = "-- ms";
        if (_cbSpotify.IsChecked == true)
            tasks.Add(TestConnectivityAsync("Spotify", "open.spotify.com", _latencySpotify));
        else
            _latencySpotify.Text = "-- ms";
        if (_cbCloudflare.IsChecked == true)
            tasks.Add(TestConnectivityAsync("Cloudflare", "cloudflare.com", _latencyCloudflare));
        else
            _latencyCloudflare.Text = "-- ms";
        if (_cbCopilot.IsChecked == true)
            tasks.Add(TestConnectivityAsync("Copilot", "githubcopilot.com", _latencyCopilot));
        else
            _latencyCopilot.Text = "-- ms";
        await Task.WhenAll(tasks);
    }

    private async Task TestConnectivityAsync(string name, string host, TextBlock label)
    {
        var latencyText = "-- ms";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var sw = Stopwatch.StartNew();
            var resp = await http.GetAsync($"https://{host}", HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            resp.Dispose();
            latencyText = $"{sw.ElapsedMilliseconds} ms";
        }
        catch { latencyText = "超时"; }

        DispatcherQueue.TryEnqueue(() =>
        {
            label.Text = latencyText;
            if (latencyText.EndsWith("ms") && int.TryParse(latencyText.Replace(" ms", ""), out var t))
            {
                if (t < 200) label.Foreground = new SolidColorBrush(Colors.Green);
                else if (t < 600) label.Foreground = new SolidColorBrush(Colors.Orange);
                else label.Foreground = new SolidColorBrush(Colors.Red);
            }
            else
            {
                label.Foreground = new SolidColorBrush(Colors.Red);
            }
        });
    }

    private void ShowMessage(string message)
    {
        _mainWindow.ShowMessageDialog(message);
    }
}
