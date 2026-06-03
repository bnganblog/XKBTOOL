using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolboxWinUI.Models;
using WColor = Windows.UI.Color;
using IO = System.IO;

namespace ToolboxWinUI.Pages;

public sealed partial class SettingsPage : UserControl
{
    private readonly MainWindow _mainWindow;
    private readonly List<ToolInfo> _tools;
    private readonly SolidColorBrush _cardBorderBrush = new(WColor.FromArgb(255, 220, 220, 220));

    public SettingsPage(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _tools = mainWindow.GetTools();
        verText.Text = GetVersion();

        if (App.CurrentTheme == "DarkGlass") themeCombo.SelectedIndex = 1;
        else if (App.CurrentTheme == "LightGlass") themeCombo.SelectedIndex = 2;
        else themeCombo.SelectedIndex = 0;

        sysInfoStyleCombo.SelectedIndex = App.SysInfoStyle == "Bar" ? 1 : 0;

        proxyUrlBox.Text = App.DownloadProxy;
        saveProxyBtn.Click += (_, _) =>
        {
            App.SetDownloadProxy(proxyUrlBox.Text);
            proxyUrlBox.Text = App.DownloadProxy;
            ShowTip("下载代理已保存。");
        };
        resetProxyBtn.Click += (_, _) =>
        {
            App.SetDownloadProxy("https://ghfast.top/");
            proxyUrlBox.Text = App.DownloadProxy;
            ShowTip("已恢复默认代理。");
        };

        UpdateCardStyle(App.CurrentTheme);

        themeCombo.SelectionChanged += (_, _) =>
        {
            var theme = themeCombo.SelectedIndex switch
            {
                1 => "DarkGlass",
                2 => "LightGlass",
                _ => "Light"
            };
            App.SetTheme(theme);
            UpdateCardStyle(theme);
        };

        sysInfoStyleCombo.SelectionChanged += (_, _) =>
        {
            var style = sysInfoStyleCombo.SelectedIndex == 1 ? "Bar" : "Circular";
            App.SetSysInfoStyle(style);
            _mainWindow.InvalidateSysInfoCache();
        };

        App.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;

        resetBtn.Click += ResetBtn_Click;
        exportBtn.Click += ExportBtn_Click;
        importBtn.Click += ImportBtn_Click;
        checkUpdateBtn.Click += CheckUpdateBtn_Click;
    }

    private void OnThemeChanged()
    {
        _ = DispatcherQueue.TryEnqueue(() => UpdateCardStyle(App.CurrentTheme));
    }

    private void UpdateCardStyle(string theme)
    {
        bool darkGlass = theme == "DarkGlass";
        bool lightGlass = theme == "LightGlass";
        bool glass = darkGlass || lightGlass;

        if (glass)
        {
            var bgColor = darkGlass
                ? WColor.FromArgb(40, 0, 0, 0)
                : WColor.FromArgb(60, 255, 255, 255);
            foreach (var card in new[] { aboutCard, updateCard, themeCard, chartStyleCard, proxyCard, toolsCard })
            {
                card.Background = new SolidColorBrush(bgColor);
                card.BorderBrush = darkGlass ? null : _cardBorderBrush;
            }
            _cardBorderBrush.Color = darkGlass
                ? WColor.FromArgb(0, 0, 0, 0)
                : WColor.FromArgb(60, 150, 150, 150);
        }
        else
        {
            _cardBorderBrush.Color = WColor.FromArgb(255, 220, 220, 220);
            foreach (var card in new[] { aboutCard, updateCard, themeCard, chartStyleCard, proxyCard, toolsCard })
            {
                card.Background = new SolidColorBrush(WColor.FromArgb(255, 255, 255, 255));
                card.BorderBrush = _cardBorderBrush;
            }
        }
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.ResetTools();
        _mainWindow.RefreshCurrentPage();
        ShowTip("已恢复默认工具列表。");
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON 文件", new List<string> { ".json" });
        picker.SuggestedFileName = "tools";
        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            var json = JsonSerializer.Serialize(_mainWindow.GetTools(), new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(file.Path, json);
            ShowTip("导出成功！");
        }
    }

    private async void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file.Path);
                var tools = JsonSerializer.Deserialize<List<ToolInfo>>(json);
                if (tools != null && tools.Count > 0)
                {
                    _mainWindow.SetTools(tools);
                    _mainWindow.RefreshCurrentPage();
                    ShowTip($"导入成功！共 {tools.Count} 个工具。");
                }
            }
            catch
            {
                ShowTip("导入失败，文件格式不正确。");
            }
        }
    }

    private async void ShowTip(string message)
    {
        var tip = new ContentDialog
        {
            Title = "提示",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = _mainWindow.GetContentXamlRoot()
        };
        await tip.ShowAsync();
    }

    private static string GetVersion()
    {
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null) return $"显卡吧工具箱WinUI3 v{ver.Major}.{ver.Minor}.{ver.Build}";
        }
        catch { }
        return "显卡吧工具箱WinUI3";
    }

    private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        checkUpdateBtn.IsEnabled = false;
        updateStatusText.Text = "正在检查更新...";

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XKBToolbox");
            client.Timeout = TimeSpan.FromSeconds(15);

            var json = await client.GetStringAsync("https://api.github.com/repos/bnganblog/xkbtool/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var releaseName = root.GetProperty("name").GetString() ?? "";
            var releaseUrl = root.GetProperty("html_url").GetString() ?? "";
            var body = root.GetProperty("body").GetString() ?? "";

            var currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var latestVer = ParseVersion(tagName);

            if (latestVer != null && currentVer != null && latestVer > currentVer)
            {
                var downloadUrl = "";
                var assets = root.GetProperty("assets");
                if (assets.GetArrayLength() > 0)
                {
                    downloadUrl = assets[0].GetProperty("browser_download_url").GetString() ?? "";
                }

                updateStatusText.Text = $"发现新版本: {tagName}";
                ShowUpdateDialog(releaseName, tagName, body, downloadUrl, releaseUrl);
            }
            else
            {
                updateStatusText.Text = $"已是最新版本 ({tagName})";
                ShowTip("已是最新版本！");
            }
        }
        catch (Exception ex)
        {
            updateStatusText.Text = "检查更新失败";
            ShowTip($"检查更新失败: {ex.Message}");
        }
        finally
        {
            checkUpdateBtn.IsEnabled = true;
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var v = tag.TrimStart('v', 'V');
        if (Version.TryParse(v, out var ver))
            return ver;
        if (Version.TryParse(v + ".0", out ver))
            return ver;
        return null;
    }

    private async void ShowUpdateDialog(string releaseName, string tagName, string body, string downloadUrl, string releaseUrl)
    {
        var dialog = new ContentDialog
        {
            Title = $"发现新版本 {tagName}",
            CloseButtonText = "关闭",
            PrimaryButtonText = "下载更新",
            SecondaryButtonText = "查看详情",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _mainWindow.GetContentXamlRoot()
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = releaseName,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (!string.IsNullOrEmpty(body))
        {
            var bodyText = new TextBlock
            {
                Text = body.Length > 500 ? body[..500] + "..." : body,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                MaxHeight = 200
            };
            content.Children.Add(bodyText);
        }

        dialog.Content = content;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(downloadUrl))
        {
            await DownloadUpdate(downloadUrl, tagName);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
        }
    }

    private async Task DownloadUpdate(string url, string tagName)
    {
        updateStatusText.Text = "正在下载更新...";
        checkUpdateBtn.IsEnabled = false;

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XKBToolbox");
            client.Timeout = TimeSpan.FromMinutes(5);

            var tempDir = IO.Path.Combine(IO.Path.GetTempPath(), "XKBToolbox_Update");
            Directory.CreateDirectory(tempDir);
            var fileName = IO.Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrEmpty(fileName)) fileName = $"XKBToolbox_{tagName}.zip";
            var filePath = IO.Path.Combine(tempDir, fileName);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var fs = File.Create(filePath);
            await response.Content.CopyToAsync(fs);

            updateStatusText.Text = $"已下载: {fileName}";

            var tip = new ContentDialog
            {
                Title = "下载完成",
                Content = $"更新文件已保存到:\n{filePath}\n\n是否打开所在文件夹？",
                PrimaryButtonText = "打开文件夹",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _mainWindow.GetContentXamlRoot()
            };
            if (await tip.ShowAsync() == ContentDialogResult.Primary)
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
        }
        catch (Exception ex)
        {
            updateStatusText.Text = "下载失败";
            ShowTip($"下载失败: {ex.Message}");
        }
        finally
        {
            checkUpdateBtn.IsEnabled = true;
        }
    }
}
