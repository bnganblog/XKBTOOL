using System.Diagnostics;
using System.Net.Http;
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
    private Process? _helperProcess;
    private DispatcherTimer? _statusTimer;
    private bool _isRunning;

    private CheckBox _cbGitHub = null!;
    private CheckBox _cbSteam = null!;
    private CheckBox _cbSpotify = null!;
    private CheckBox _cbCloudflare = null!;
    private Button _startBtn = null!;
    private Button _stopBtn = null!;
    private TextBlock _statusText = null!;
    private TextBlock _logText = null!;

    private readonly SolidColorBrush _accentBrush = new(Color.FromArgb(255, 0, 120, 212));
    private readonly SolidColorBrush _greenBrush = new(Color.FromArgb(255, 0, 165, 0));
    private readonly SolidColorBrush _redBrush = new(Color.FromArgb(255, 232, 17, 35));
    private readonly SolidColorBrush _textPrimaryBrush = new(Color.FromArgb(255, 30, 30, 30));
    private readonly SolidColorBrush _textSecondaryBrush = new(Color.FromArgb(255, 100, 100, 100));
    private readonly SolidColorBrush _cardBgBrush = new(Color.FromArgb(255, 255, 255, 255));
    private readonly SolidColorBrush _cardBorderBrush = new(Color.FromArgb(255, 220, 220, 220));

    private static readonly string HelperPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "AcceleratorHelper", "AcceleratorHelper.exe");

    public AcceleratorPage(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        Margin = new Thickness(24);
        BuildUI();
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
        _cbGitHub = new CheckBox { Content = "GitHub", IsChecked = true };
        _cbSteam = new CheckBox { Content = "Steam", IsChecked = true };
        _cbSpotify = new CheckBox { Content = "Spotify" };
        _cbCloudflare = new CheckBox { Content = "Cloudflare" };
        servicePanel.Children.Add(_cbGitHub);
        servicePanel.Children.Add(_cbSteam);
        servicePanel.Children.Add(_cbSpotify);
        servicePanel.Children.Add(_cbCloudflare);
        stack.Children.Add(servicePanel);

        // 控制
        var controlPanel = CreateCard();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _startBtn = CreateButton("启动加速", _greenBrush, Colors.White);
        _startBtn.Click += StartBtn_Click;
        _stopBtn = CreateButton("停止加速", _redBrush, Colors.White);
        _stopBtn.Click += StopBtn_Click;
        _stopBtn.IsEnabled = false;
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

    private void ShowMessage(string message)
    {
        _mainWindow.ShowMessageDialog(message);
    }
}
