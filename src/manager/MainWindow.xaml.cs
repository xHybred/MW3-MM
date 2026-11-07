using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MW3ModManager;

public partial class MainWindow : Window
{
    private const float PivotFov = 65f;

    private ModConfig _config = new();
    private string? _gameDir;
    private bool _loading = true;

    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public MainWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveConfig(); };
        Loaded += OnLoaded;
        Closed += (_, _) => { _statusTimer.Stop(); _saveTimer.Stop(); };
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _gameDir = GameLocator.FindGame();
        _config = ModConfig.Load();

        _loading = true;
        FovSlider.Value = Math.Clamp(_config.Fov, 60f, 160f);
        AimSlider.Value = Math.Clamp(_config.AimZoom * 100f, 100f, 200f);
        EnabledSwitch.IsChecked = _config.Enabled;
        AffectedSwitch.IsChecked = _config.Affected;
        FlatSwitch.IsChecked = _config.AimFlat;
        _loading = false;

        if (!File.Exists(ModConfig.SettingsPath))
        {
            try { _config.Save(); } catch (IOException) { /* the panel still shows defaults */ }
        }

        Refresh();
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
    }

    private void Refresh()
    {
        bool found = _gameDir is not null;
        GamePathText.Text = found
            ? _gameDir!
            : "Not found - use Change... and pick the folder that contains iw5sp.exe";

        RefreshRuntimeRow();
        RefreshDxvkRow();
        UpdateFovUi();
    }

    private void RefreshRuntimeRow()
    {
        string? installed = RuntimeInstaller.ProxyVersion(_gameDir);
        string? bundled = RuntimeInstaller.BundledVersion;

        RuntimeDot.Fill = Brush(installed is not null ? "Accent" : "Dim");
        RuntimeStateText.Text = installed is null
            ? "Runtime not installed"
            : "Runtime " + installed["MW3Mod ".Length..] + " installed";

        if (installed is null)
        {
            RuntimeButton.Content = "Install";
            RuntimeButton.IsEnabled = _gameDir is not null && bundled is not null;
            RuntimeDetail.Text = bundled is null
                ? "MW3Mod.dll is missing next to the manager, rebuild it first."
                : "Adds X3DAudio1_7.dll to the game folder. The game loads it on every launch, however you start it.";
        }
        else if (bundled is not null && bundled != installed)
        {
            RuntimeButton.Content = "Update";
            RuntimeButton.IsEnabled = true;
            RuntimeDetail.Text = $"Installed {installed}, this build ships {bundled}.";
        }
        else
        {
            RuntimeButton.Content = "Remove";
            RuntimeButton.IsEnabled = true;
            RuntimeDetail.Text = "Installed. The FOV override applies from the next launch onward.";
        }
    }

    private void RefreshDxvkRow()
    {
        bool installed = RuntimeInstaller.DxvkInstalled(_gameDir);
        DxvkButton.Content = installed ? "Remove" : "Install from file...";
        DxvkButton.IsEnabled = _gameDir is not null;
        DxvkDetail.Text = installed
            ? "Installed as d3d9.dll."
            : "Optional. Drop in DXVK's d3d9.dll to render through Vulkan instead of D3D9.";
    }

    private void UpdateFovUi()
    {
        bool runtime = RuntimeInstaller.ProxyVersion(_gameDir) is not null;
        FovPanel.IsEnabled = runtime;
        FovPanel.Opacity = runtime ? 1.0 : 0.45;

        float fov = (float)Math.Round(FovSlider.Value);
        float zoom = _config.AimZoom;
        float ads = _config.Affected ? fov / zoom : PivotFov / zoom;

        FovValueText.Text = fov.ToString("0") + "\u00B0";
        HipText.Text = fov.ToString("0.0") + "\u00B0";
        AdsText.Text = ads.ToString("0.0") + "\u00B0";
        AimValueText.Text = Math.Round(zoom * 100f).ToString("0") + "%";

        AffectedSwitch.Content = _config.Affected
            ? "Scale aiming FOV with your FOV"
            : "Keep aiming FOV fixed";
        AdsHint.Text = _config.Affected
            ? "Aiming widens with your FOV, like newer Call of Duty titles."
            : "Aiming keeps the default 65\u00B0 base, so only the hipfire view changes.";

        AimHint.Text = _config.AimFlat
            ? "Every weapon zooms the same, scopes included."
            : "1x sights share one zoom level, scopes keep more of their magnification.";
    }

    private void RefreshStatus()
    {
        bool running = Process.GetProcessesByName("iw5sp").Length > 0;
        GameDot.Fill = Brush(running ? "Accent" : "Dim");
        GameStateText.Text = running ? "Game running" : "Game not running";

        string? line = LastLogLine();
        if (running && line is not null)
            StatusText.Text = line;
        else if (line is not null)
            StatusText.Text = "Last session: " + line;
        else
            StatusText.Text = "Ready";

        // The installed DLL is easy to swap, so recheck while the window is open.
        RefreshRuntimeRow();
    }

    private void FovSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.Fov = (float)Math.Round(e.NewValue);
        UpdateFovUi();
        SaveLater();
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.Enabled = EnabledSwitch.IsChecked == true;
        _config.Affected = AffectedSwitch.IsChecked == true;
        _config.AimFlat = FlatSwitch.IsChecked == true;
        UpdateFovUi();
        SaveLater();
    }

    private void AimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.AimZoom = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void SaveLater()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveConfig()
    {
        try
        {
            _config.Save();
            StatusText.Text = "Saved. A running game picks this up within a second.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not write settings: " + ex.Message;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Pick the Modern Warfare 3 folder",
            Multiselect = false,
        };
        if (_gameDir is not null) dialog.InitialDirectory = _gameDir;

        if (dialog.ShowDialog(this) != true) return;

        if (!GameLocator.IsGameFolder(dialog.FolderName))
        {
            MessageBox.Show(this,
                "That folder does not contain " + GameLocator.ExeName + ".",
                "MW3 Mod Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _gameDir = dialog.FolderName;
        Refresh();
    }

    private void Runtime_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null) return;
        if (GuardGameRunning()) return;

        try
        {
            if (RuntimeInstaller.ProxyVersion(_gameDir) is not null)
            {
                RuntimeInstaller.RemoveProxy(_gameDir);
                StatusText.Text = "Runtime removed. The game is back to vanilla FOV.";
            }
            else
            {
                RuntimeInstaller.InstallProxy(_gameDir);
                StatusText.Text = "Runtime installed. Start the game however you like.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or FileNotFoundException)
        {
            Fail("Could not change the runtime", ex);
        }

        Refresh();
    }

    private void Dxvk_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null) return;
        if (GuardGameRunning()) return;

        try
        {
            if (RuntimeInstaller.DxvkInstalled(_gameDir))
            {
                RuntimeInstaller.RemoveDxvk(_gameDir);
                StatusText.Text = "DXVK removed.";
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Pick DXVK's x64 d3d9.dll",
                    Filter = "d3d9.dll|d3d9.dll|DLL files|*.dll",
                    CheckFileExists = true,
                };
                if (dialog.ShowDialog(this) != true) return;

                RuntimeInstaller.InstallDxvk(_gameDir, dialog.FileName);
                StatusText.Text = "DXVK installed as d3d9.dll.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Fail("Could not change DXVK", ex);
        }

        Refresh();
    }

    private bool GuardGameRunning()
    {
        if (Process.GetProcessesByName("iw5sp").Length == 0) return false;

        MessageBox.Show(this,
            "Close Modern Warfare 3 first - it holds the files open while it runs.",
            "MW3 Mod Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    private void Fail(string title, Exception ex)
    {
        StatusText.Text = ex.Message;
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e) => OpenPath(ModConfig.LogPath);

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenPath(ModConfig.DataDir);

    private void OpenPath(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else StatusText.Text = "Nothing to open yet at " + path;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "Could not open " + path;
        }
    }

    // The log is UTF-16 and is written while the game runs, so read the tail
    // with a sharing mode and drop a partial line from the cut point.
    private static string? LastLogLine()
    {
        string path = ModConfig.LogPath;
        try
        {
            if (!File.Exists(path)) return null;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long length = stream.Length;
            if (length < 2) return null;

            int take = (int)Math.Min(length, 4096);
            long start = length - take;
            if (start % 2 != 0) { start++; take--; }

            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[take];
            int read = stream.Read(buffer, 0, take);
            string text = Encoding.Unicode.GetString(buffer, 0, read);

            string[] lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim('\r', ' ', '\0');
                if (line.Length == 0) continue;
                int close = line.IndexOf(']');
                return close > 0 ? line[(close + 1)..].Trim() : line;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private SolidColorBrush Brush(string key) => (SolidColorBrush)FindResource(key);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void EnableDarkTitleBar()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int enabled = 1;
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
    }
}
