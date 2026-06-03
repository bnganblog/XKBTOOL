using System.Text.Json;
using Microsoft.UI.Xaml;

namespace ToolboxWinUI;

public partial class App : Application
{
    private static readonly Mutex _singleInstanceMutex = new(true, @"Global\XKBToolbox_SingleInstance");
    private static readonly string SettingsFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ToolboxWinUI", "settings.json");

    public static string CurrentTheme { get; private set; } = "LightGlass";
    public static string SysInfoStyle { get; private set; } = "Circular";
    public static string DownloadProxy { get; private set; } = "https://ghfast.top/";
    public static event Action? ThemeChanged;

    public App()
    {
        InitializeComponent();
        if (!_singleInstanceMutex.WaitOne(TimeSpan.Zero, false))
        {
            _singleInstanceMutex.Dispose();
            Environment.Exit(0);
        }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs e)
    {
        var window = new MainWindow();
        window.Activate();
        LoadSavedTheme();
    }

    public static void SetTheme(string themeName)
    {
        CurrentTheme = themeName switch
        {
            "DarkGlass" => "DarkGlass",
            "LightGlass" => "LightGlass",
            _ => "Light"
        };
        SaveSetting("theme", CurrentTheme);
        ThemeChanged?.Invoke();
    }

    public static void SetSysInfoStyle(string style)
    {
        SysInfoStyle = style == "Bar" ? "Bar" : "Circular";
        SaveSetting("sysInfoStyle", SysInfoStyle);
    }

    public static void SetDownloadProxy(string proxy)
    {
        DownloadProxy = string.IsNullOrWhiteSpace(proxy) ? "" : proxy.TrimEnd('/') + "/";
        SaveSetting("downloadProxy", DownloadProxy);
    }

    public static void LoadSavedTheme()
    {
        var theme = LoadSetting("theme");
        if (theme == "DarkGlass")
            SetTheme("DarkGlass");
        else if (theme == "LightGlass" || string.IsNullOrEmpty(theme))
            SetTheme("LightGlass");
        else
            SetTheme("Light");

        var style = LoadSetting("sysInfoStyle");
        SysInfoStyle = style == "Bar" ? "Bar" : "Circular";

        var proxy = LoadSetting("downloadProxy");
        if (!string.IsNullOrEmpty(proxy))
            DownloadProxy = proxy.TrimEnd('/') + "/";
    }

    private static void SaveSetting(string key, string value)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(SettingsFile);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            var data = new Dictionary<string, string>();
            if (System.IO.File.Exists(SettingsFile))
                data = JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(SettingsFile)) ?? [];
            data[key] = value;
            System.IO.File.WriteAllText(SettingsFile, JsonSerializer.Serialize(data));
        }
        catch { }
    }

    private static string LoadSetting(string key)
    {
        try
        {
            if (System.IO.File.Exists(SettingsFile))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(SettingsFile));
                if (data != null && data.TryGetValue(key, out var val))
                    return val;
            }
        }
        catch { }
        return null;
    }
}
