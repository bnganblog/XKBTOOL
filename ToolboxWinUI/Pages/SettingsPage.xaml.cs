using System.Text.Json;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ToolboxWinUI.Models;
using WColor = Windows.UI.Color;

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

        App.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => App.ThemeChanged -= OnThemeChanged;

        resetBtn.Click += ResetBtn_Click;
        exportBtn.Click += ExportBtn_Click;
        importBtn.Click += ImportBtn_Click;
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
            foreach (var card in new[] { aboutCard, themeCard, toolsCard })
            {
                card.Background = null;
                card.BorderBrush = darkGlass ? null : _cardBorderBrush;
            }
            _cardBorderBrush.Color = darkGlass
                ? WColor.FromArgb(0, 0, 0, 0)
                : WColor.FromArgb(60, 150, 150, 150);
        }
        else
        {
            _cardBorderBrush.Color = WColor.FromArgb(255, 220, 220, 220);
            foreach (var card in new[] { aboutCard, themeCard, toolsCard })
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
}
