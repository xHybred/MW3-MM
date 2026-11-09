using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    private bool _rendererBusy;
    private bool _gameWasRunning;
    private long _statusHoldUntil;

    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public MainWindow()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveConfig(); };
        Loaded += OnLoaded;
        Closed += (_, _) => { _statusTimer.Stop(); _saveTimer.Stop(); Log.Info("Manager closed."); };
        SourceInitialized += (_, _) => EnableDarkTitleBar();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _gameDir = GameLocator.FindGame();
        _config = ModConfig.Load();

        Log.Info("Game folder: " + (_gameDir ?? "not found"));
        Log.Info("Settings file: " + (File.Exists(ModConfig.SettingsPath) ? "found" : "missing"));
        Log.Info("Bundled runtime: " + (RuntimeInstaller.BundledVersion ?? "missing"));
        if (_gameDir is not null)
        {
            Log.Info("Installed runtime: " + (RuntimeInstaller.ProxyVersion(_gameDir) ?? "none"));
            Log.Info("d3d9 wrapper: " + RuntimeInstaller.OwnerName(RuntimeInstaller.Owner(_gameDir)));
            Log.Info("ReShade: " + RuntimeInstaller.InstalledMode(_gameDir));
        }
        else
        {
            Log.Warn("Modern Warfare 3 was not found in the usual Steam locations.");
        }

        // config.cfg is what the game reads before its first frame, so when it
        // exists it is the truth about the intro setting, not the ini.
        if (IntroOption.ConfigPath(_gameDir) is string introConfig && File.Exists(introConfig))
            _config.SkipIntro = IntroOption.IsSkipped(_gameDir);

        // These cards no longer have a master switch: the sliders are the whole
        // control, and the stock end of each slider is what leaves the game alone.
        _config.FlinchEnabled = true;
        _config.SpreadEnabled = true;
        _config.RecoilEnabled = true;
        _config.SwayEnabled = true;

        // The blur slider carries the off state (0 = no blur), so a saved
        // "Enabled=0" from the old switch becomes a slider at the bottom.
        if (!_config.BlurEnabled) _config.BlurStrength = 0f;
        _config.BlurEnabled = true;

        _loading = true;
        ApplyConfigToControls();
        _loading = false;

        if (!File.Exists(ModConfig.SettingsPath))
        {
            try
            {
                _config.Save();
                Log.Info("Default settings written.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Could not write default settings", ex);
            }
        }

        Log.Info($"Settings: fov={_config.Fov}, aim={_config.AimZoom}, flinch={_config.FlinchStrength}, blur={_config.BlurStrength}, recoil={_config.RecoilStrength}, sway={_config.SwayStrength}, spread={_config.SpreadAccuracy}/{_config.SpreadAimAccuracy}/{_config.SpreadBloom}, crosshair={_config.CrosshairSize}/{_config.CrosshairOpacity}/{_config.CrosshairStyle}");

        BuildPresetBox();
        Refresh();
        ShowPage(0);
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
        Log.Info("Manager ready.");
    }

    /// <summary>Pushes the loaded configuration into every control at once.</summary>
    private void ApplyConfigToControls()
    {
        IntroSwitch.IsChecked = _config.SkipIntro;
        FovSlider.Value = Math.Clamp(_config.Fov, 60f, ModConfig.MaxFov);
        AimSlider.Value = Math.Clamp(_config.AimZoom * 100f, 100f, ModConfig.MaxZoom * 100f);
        EnabledSwitch.IsChecked = _config.Enabled;
        AffectedSwitch.IsChecked = _config.Affected;
        AimSwitch.IsChecked = _config.AimEnabled;
        FlatSwitch.IsChecked = _config.AimFlat;
        FlinchSlider.Value = Math.Clamp(_config.FlinchStrength * 100f, 0f, 100f);
        BlurSlider.Value = Math.Clamp(_config.BlurStrength * 100f, 0f, 100f);
        RecoilSlider.Value = Math.Clamp(_config.RecoilStrength * 100f, 0f, 100f);
        SwaySlider.Value = Math.Clamp(_config.SwayStrength * 100f, 0f, 100f);
        FlashSwitch.IsChecked = !_config.FlashEnabled;
        SpreadSlider.Value = Math.Clamp(_config.SpreadAccuracy * 100f, 0f, 100f);
        SpreadAimSlider.Value = Math.Clamp(_config.SpreadAimAccuracy * 100f, 0f, 100f);
        BloomSlider.Value = Math.Clamp(_config.SpreadBloom * 100f, 0f, 100f);
        ViewmodelXSlider.Value = Math.Clamp(_config.ViewmodelX, -1.5f, 3f);
        ViewmodelZSlider.Value = Math.Clamp(_config.ViewmodelZ, -3f, 3f);
        ViewmodelYSlider.Value = Math.Clamp(_config.ViewmodelY, -3f, 3f);
        ViewmodelSwitch.IsChecked = _config.ViewmodelEnabled;
        CrosshairSizeSlider.Value = Math.Clamp(_config.CrosshairSize * 100f, 50f, 200f);
        CrosshairOpacitySlider.Value = Math.Clamp(_config.CrosshairOpacity * 100f, 0f, 100f);
        CrosshairShowSwitch.IsChecked = _config.CrosshairShow;
        CrosshairDynamicSwitch.IsChecked = _config.CrosshairDynamic;
        CrosshairSwitch.IsChecked = _config.CrosshairEnabled;
    }

    private readonly Button[] _navButtons = new Button[6];
    private readonly FrameworkElement[] _pages = new FrameworkElement[6];

    // True while the box is being pointed at a preset by code, so the
    // SelectionChanged that follows does not apply it back over the live values.
    private bool _presetUpdating;
    private int _page;

    private void ShowPage(int index)
    {
        _navButtons[0] = NavAim;
        _navButtons[1] = NavWeapon;
        _navButtons[2] = NavViewmodel;
        _navButtons[3] = NavCrosshair;
        _navButtons[4] = NavScreen;
        _navButtons[5] = NavSetup;
        _pages[0] = PageAim;
        _pages[1] = PageWeapon;
        _pages[2] = PageViewmodel;
        _pages[3] = PageCrosshair;
        _pages[4] = PageScreen;
        _pages[5] = PageSetup;

        _page = index;
        for (int i = 0; i < _pages.Length; ++i)
        {
            bool active = i == index;
            _pages[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            _navButtons[i].Background = (Brush)FindResource(active ? "CardBorder" : "CardBg");
            _navButtons[i].Foreground = (Brush)FindResource(active ? "Text" : "Muted");
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(sender == NavWeapon ? 1 : sender == NavViewmodel ? 2 : sender == NavCrosshair ? 3
            : sender == NavScreen ? 4 : sender == NavSetup ? 5 : 0);
    }

    /// <summary>
    /// Fills the preset list from <see cref="Presets.All"/>, so adding a preset
    /// is one entry in that list and nothing here. "Custom" is the state where
    /// the live values no longer match any of them.
    /// </summary>
    private void BuildPresetBox()
    {
        _presetUpdating = true;
        PresetBox.Items.Clear();
        foreach (Preset preset in Presets.All)
            PresetBox.Items.Add(preset.Name);
        PresetBox.Items.Add("Custom");
        _presetUpdating = false;

        RefreshPresetBox();
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _presetUpdating)
            return;

        int index = PresetBox.SelectedIndex;
        if (index < 0 || index >= Presets.All.Count)
            return;

        ApplyPreset(Presets.All[index]);
    }

    private void ApplyPreset(Preset preset)
    {
        _config.CopyFeatureValuesFrom(preset.Values);

        _loading = true;
        ApplyConfigToControls();
        _loading = false;

        _saveTimer.Stop();
        Log.Info("Preset applied: " + preset.Name);
        SaveConfig();
        StatusText.Text = preset.Name + " applied. A running game picks this up within a second.";
        _statusHoldUntil = Environment.TickCount64 + 4000;
        UpdateFovUi();
        RefreshPresetBox();
    }

    /// <summary>Points the list at the preset the live values still match.</summary>
    private void RefreshPresetBox()
    {
        int active = -1;
        for (int i = 0; i < Presets.All.Count; ++i)
        {
            if (_config.MatchesFeatureValues(Presets.All[i].Values))
            {
                active = i;
                break;
            }
        }

        bool custom = active < 0;
        _presetUpdating = true;
        PresetBox.SelectedIndex = custom ? PresetBox.Items.Count - 1 : active;
        _presetUpdating = false;

        PresetBox.ToolTip = custom
            ? "The live values do not match a preset."
            : Presets.All[active].Description;
    }

    private void Refresh()
    {
        bool found = _gameDir is not null;
        GamePathText.Text = found
            ? _gameDir!
            : "Not found - use Change... and pick the folder that contains iw5sp.exe";

        RefreshRuntimeRow();
        RefreshDxvkRow();
        RefreshD3d11Row();
        RefreshReShadeRow();
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
            RuntimeDetail.Text = "Installed. Settings are picked up while the game runs.";
        }
    }

    private void RefreshDxvkRow()
    {
        bool installed = RuntimeInstaller.DxvkInstalled(_gameDir);
        string? version = RuntimeInstaller.DxvkVersion(_gameDir);

        bool reShade = RuntimeInstaller.InstalledMode(_gameDir) != RuntimeInstaller.ReShadeMode.None;

        DxvkButton.Content = installed ? "Remove" : "Install";
        DxvkButton.IsEnabled = _gameDir is not null && !_rendererBusy && (installed || !reShade);
        DxvkFileButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        DxvkFileButton.IsEnabled = _gameDir is not null && !_rendererBusy && (installed || !reShade);

        DxvkDetail.Text = installed
            ? version is null ? "Installed as d3d9.dll." : $"DXVK {version} installed as d3d9.dll."
            : reShade
                ? "ReShade is installed. Remove it first; DXVK needs ReShade's Vulkan install to run together."
                : "Optional. Downloads the latest release from GitHub and renders through Vulkan.";
    }

    private void RefreshD3d11Row()
    {
        bool installed = RuntimeInstaller.D3d11Installed(_gameDir);
        string? version = RuntimeInstaller.D3d11Version(_gameDir);

        D3d11Button.Content = installed ? "Remove" : "Install";
        D3d11Button.IsEnabled = _gameDir is not null && !_rendererBusy;
        D3d11FileButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        D3d11FileButton.IsEnabled = _gameDir is not null && !_rendererBusy;

        D3d11Detail.Text = installed
            ? version is null ? "dgVoodoo installed as d3d9.dll." : $"dgVoodoo {version} installed as d3d9.dll."
            : "Optional. Hands the game's Direct3D 9 calls to Direct3D 11 through dgVoodoo 2.";
    }

    private void RefreshReShadeRow()
    {
        var mode = RuntimeInstaller.InstalledMode(_gameDir);
        string? version = RuntimeInstaller.ReShadeVersion(_gameDir);
        string tag = version is null ? "ReShade" : "ReShade " + version;

        if (mode != RuntimeInstaller.ReShadeMode.None)
        {
            ReShadeButton.Content = "Remove";
            ReShadeButton.IsEnabled = _gameDir is not null && !_rendererBusy;
            ReShadeDetail.Text = mode == RuntimeInstaller.ReShadeMode.Direct3D11
                ? $"{tag} installed as dxgi.dll next to the Direct3D 11 wrapper."
                : $"{tag} installed as d3d9.dll for native Direct3D 9.";
            return;
        }

        ReShadeButton.Content = "Install";

        string? blocker = RuntimeInstaller.ReShadeBlocker(_gameDir);
        ReShadeButton.IsEnabled = _gameDir is not null && !_rendererBusy && blocker is null;

        if (_gameDir is null)
        {
            ReShadeDetail.Text = "Optional. Adds ReShade with its presets and shader library.";
        }
        else if (blocker is not null)
        {
            ReShadeDetail.Text = blocker;
        }
        else
        {
            ReShadeDetail.Text = RuntimeInstaller.TargetMode(_gameDir) == RuntimeInstaller.ReShadeMode.Direct3D11
                ? "Optional. Installs ReShade for Direct3D 11 as dxgi.dll next to dgVoodoo, with the included presets and shaders."
                : "Optional. Installs ReShade for native Direct3D 9 as d3d9.dll, with the included presets and shaders.";
        }
    }

    private void UpdateFovUi()
    {
        bool runtime = RuntimeInstaller.ProxyVersion(_gameDir) is not null;
        FovPanel.IsEnabled = runtime;
        FovPanel.Opacity = runtime ? 1.0 : 0.45;

        float fov = (float)Math.Round(FovSlider.Value);
        float zoom = _config.AimZoom;
        // With the aiming hook off the engine keeps its own 40 degree weapon FOV,
        // so ADS tracks cg_fovScale only.
        float ads = !_config.AimEnabled
            ? (_config.Affected ? fov / 1.625f : 40f)
            : (_config.Affected ? fov / zoom : PivotFov / zoom);

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

        AimHint.Text = !_config.AimEnabled
            ? "Stock. Each weapon keeps the zoom the game gives it."
            : _config.AimFlat
                ? "Every weapon zooms the same, scopes included."
                : "1x sights share one zoom level, scopes keep more of their magnification.";
        AimSlider.IsEnabled = _config.AimEnabled;
        AimSlider.Opacity = _config.AimEnabled ? 1.0 : 0.45;
        FlatSwitch.IsEnabled = _config.AimEnabled;
        FlatSwitch.Opacity = _config.AimEnabled ? 1.0 : 0.45;

        FlinchPanel.IsEnabled = runtime;
        FlinchPanel.Opacity = runtime ? 1.0 : 0.45;
        FlinchValueText.Text = Math.Round(FlinchSlider.Value).ToString("0") + "%";
        FlinchHint.Text = _config.FlinchStrength >= 0.999f
            ? "Stock. Damage kicks the view as hard as it always did."
            : _config.FlinchStrength <= 0.001f
                ? "No view kick at all when you take damage."
                : "Damage kicks the view less, and the minimum kick shrinks with it.";

        BlurValueText.Text = Math.Round(BlurSlider.Value).ToString("0") + "%";
        BlurHint.Text = _config.BlurStrength >= 0.999f
            ? "Stock. A hit washes the screen out as before."
            : _config.BlurStrength <= 0.001f
                ? "No blur when you are shot."
                : "The wash is lighter, so the screen stays readable.";

        SpreadPanel.IsEnabled = runtime;
        SpreadPanel.Opacity = runtime ? 1.0 : 0.45;
        SpreadValueText.Text = Math.Round(SpreadSlider.Value).ToString("0") + "%";
        SpreadHint.Text = _config.SpreadAccuracy <= 0.001f
            ? "Stock. Hipfire keeps the cone the weapon data gives it."
            : _config.SpreadAccuracy >= 0.999f
                ? "Every hipfire shot goes through the crosshair."
                : "The hipfire cone tightens, so shots land closer to the crosshair.";
        SpreadAimValueText.Text = Math.Round(SpreadAimSlider.Value).ToString("0") + "%";
        SpreadAimHint.Text = _config.SpreadAimAccuracy <= 0.001f
            ? "Stock. Aiming keeps the cone the weapon data gives it."
            : _config.SpreadAimAccuracy >= 0.999f
                ? "Every aimed shot goes through the sight."
                : "The aimed cone tightens, so shots land closer to the sight.";
        BloomValueText.Text = Math.Round(BloomSlider.Value).ToString("0") + "%";
        BloomHint.Text = _config.SpreadBloom <= 0.001f
            ? "Stock. Firing widens the cone as it always did."
            : _config.SpreadBloom >= 0.999f
                ? "Sustained fire no longer widens the cone."
                : "Firing widens the cone less.";

        RecoilPanel.IsEnabled = runtime;
        RecoilPanel.Opacity = runtime ? 1.0 : 0.45;
        ViewmodelPanel.IsEnabled = runtime;
        ViewmodelPanel.Opacity = runtime ? 1.0 : 0.45;
        RecoilValueText.Text = Math.Round(RecoilSlider.Value).ToString("0") + "%";
        RecoilHint.Text = _config.RecoilStrength >= 0.999f
            ? "Stock. The gun kicks exactly as it always did."
            : _config.RecoilStrength <= 0.001f
                ? "The gun does not kick when you fire."
                : "The gun kicks less when you fire.";

        SwayValueText.Text = Math.Round(SwaySlider.Value).ToString("0") + "%";
        SwayHint.Text = _config.SwayStrength >= 0.999f
            ? "Stock. The gun drifts exactly as it always did."
            : _config.SwayStrength <= 0.001f
                ? "The gun does not drift."
                : "The gun drifts less.";

        FlashHint.Text = _config.FlashEnabled
            ? "Stock. The flash and the smoke puff stay."
            : "No flash and no smoke puff when you fire.";

        ViewmodelXValueText.Text = Math.Round(ViewmodelXSlider.Value, 1).ToString("0.0");
        ViewmodelZValueText.Text = Math.Round(ViewmodelZSlider.Value, 1).ToString("0.0");
        ViewmodelYValueText.Text = Math.Round(ViewmodelYSlider.Value, 1).ToString("0.0");
        const float eps = 0.001f;
        ViewmodelHint.Text = Math.Abs(_config.ViewmodelZ) <= eps && Math.Abs(_config.ViewmodelY) <= eps &&
                             Math.Abs(_config.ViewmodelX) <= eps
            ? "Stock. The gun sits exactly where the game puts it."
            : "The gun sits at these offsets from its stock pose. The camera does not move.";

        CrosshairPanel.IsEnabled = runtime;
        CrosshairPanel.Opacity = runtime ? 1.0 : 0.45;
        CrosshairSizeValueText.Text = Math.Round(CrosshairSizeSlider.Value).ToString("0") + "%";
        CrosshairSizeHint.Text = _config.CrosshairSize >= 0.999f && _config.CrosshairSize <= 1.001f
            ? "Stock. The crosshair is the size the game draws it."
            : _config.CrosshairSize > 1.001f
                ? "The crosshair is drawn larger than stock."
                : "The crosshair is drawn smaller than stock.";
        CrosshairOpacityValueText.Text = Math.Round(CrosshairOpacitySlider.Value).ToString("0") + "%";

        Button[] styleButtons = { StyleGameButton, StyleSharpButton, StyleHaloButton };
        for (int i = 0; i < styleButtons.Length; ++i)
        {
            bool active = i == _config.CrosshairStyle;
            styleButtons[i].Background = (Brush)FindResource(active ? "Accent" : "CardBorder");
            styleButtons[i].Foreground = (Brush)FindResource(active ? "Bg" : "Text");
        }
        CrosshairStyleHint.Text = _config.CrosshairStyle switch
        {
            1 => "Sharp: the same four bars, drawn in screen pixels instead of an 8x8 texture.",
            2 => "Halo: a ring with four gaps and a centre dot.",
            _ => "Game: the crosshair the game draws itself.",
        };

        SetSwitchLabel(EnabledSwitch, _config.Enabled);
        SetSwitchLabel(AimSwitch, _config.AimEnabled);
        SetSwitchLabel(IntroSwitch, _config.SkipIntro);
        IntroHint.Text = _config.SkipIntro
            ? "The opening movie is skipped, so the game goes straight to the menu."
            : "The opening movie plays on launch.";
        SetSwitchLabel(FlashSwitch, !_config.FlashEnabled);
        SetSwitchLabel(ViewmodelSwitch, _config.ViewmodelEnabled);
        SetSwitchLabel(CrosshairSwitch, _config.CrosshairEnabled);
        SetSwitchLabel(CrosshairShowSwitch, _config.CrosshairShow);
        SetSwitchLabel(CrosshairDynamicSwitch, _config.CrosshairDynamic);
    }

    private static void SetSwitchLabel(CheckBox box, bool on) => box.Content = on ? "On" : "Off";

    private void RefreshStatus()
    {
        bool running = Process.GetProcessesByName("iw5sp").Length > 0;
        if (running != _gameWasRunning)
        {
            Log.Info(running ? "Game started." : "Game stopped.");
            _gameWasRunning = running;
        }
        GameDot.Fill = Brush(running ? "Accent" : "Dim");
        GameStateText.Text = running ? "Game running" : "Game not running";

        // A download or a just-applied preset owns the status line.
        if (!_rendererBusy && Environment.TickCount64 >= _statusHoldUntil)
        {
            string? line = LastLogLine();
            if (running && line is not null)
                StatusText.Text = line;
            else if (line is not null)
                StatusText.Text = "Last session: " + line;
            else
                StatusText.Text = "Ready";
        }

        // The installed DLL is easy to swap, so recheck while the window is open.
        RefreshRuntimeRow();
    }

    private void Intro_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        bool skip = IntroSwitch.IsChecked == true;
        _config.SkipIntro = skip;
        UpdateFovUi();
        SaveLater();

        if (_gameDir is null) return;
        if (Process.GetProcessesByName("iw5sp").Length > 0)
        {
            Log.Warn("Skip intro change blocked because the game is running.");
            StatusText.Text = "Close the game first - it rewrites config.cfg when it exits.";
            _statusHoldUntil = Environment.TickCount64 + 4000;
            return;
        }

        try
        {
            IntroOption.SetSkip(_gameDir, skip);
            Log.Info("Skip intro set to " + skip + ".");
            StatusText.Text = skip
                ? "The next launch goes straight to the main menu."
                : "The opening movie will play again.";
            _statusHoldUntil = Environment.TickCount64 + 4000;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not edit config.cfg", ex);
            StatusText.Text = "Could not edit config.cfg: " + ex.Message;
            _statusHoldUntil = Environment.TickCount64 + 4000;
        }
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

    private void Aim_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.AimEnabled = AimSwitch.IsChecked == true;
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

    private void FlinchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.FlinchStrength = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void BlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.BlurStrength = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void RecoilSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.RecoilStrength = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void SwaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.SwayStrength = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void Flash_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.FlashEnabled = FlashSwitch.IsChecked != true;
        UpdateFovUi();
        SaveLater();
    }

    private void Viewmodel_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.ViewmodelEnabled = ViewmodelSwitch.IsChecked == true;
        UpdateFovUi();
        SaveLater();
    }

    private void CrosshairSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.CrosshairSize = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void CrosshairOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.CrosshairOpacity = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void Crosshair_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.CrosshairEnabled = CrosshairSwitch.IsChecked == true;
        UpdateFovUi();
        SaveLater();
    }

    private void CrosshairShow_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.CrosshairShow = CrosshairShowSwitch.IsChecked == true;
        UpdateFovUi();
        SaveLater();
    }

    private void CrosshairStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.CrosshairStyle = sender == StyleSharpButton ? 1 : sender == StyleHaloButton ? 2 : 0;
        UpdateFovUi();
        SaveLater();
    }

    private void CrosshairDynamic_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.CrosshairDynamic = CrosshairDynamicSwitch.IsChecked == true;
        UpdateFovUi();
        SaveLater();
    }

    private void SpreadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.SpreadAccuracy = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void SpreadAimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.SpreadAimAccuracy = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void BloomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.SpreadBloom = (float)Math.Round(e.NewValue) / 100f;
        UpdateFovUi();
        SaveLater();
    }

    private void ViewmodelXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.ViewmodelX = (float)Math.Round(e.NewValue, 1);
        UpdateFovUi();
        SaveLater();
    }

    private void ViewmodelZSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.ViewmodelZ = (float)Math.Round(e.NewValue, 1);
        UpdateFovUi();
        SaveLater();
    }

    private void ViewmodelYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _config.ViewmodelY = (float)Math.Round(e.NewValue, 1);
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
            Log.Info("Settings saved.");
            StatusText.Text = "Saved. A running game picks this up within a second.";
            RefreshPresetBox();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not write settings", ex);
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
            Log.Warn("Rejected game folder: " + dialog.FolderName);
            MessageBox.Show(this,
                "That folder does not contain " + GameLocator.ExeName + ".",
                "MW3 Mod Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _gameDir = dialog.FolderName;
        Log.Info("Game folder set to " + _gameDir + ".");
        Refresh();
    }

    private void Runtime_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null) return;
        if (GuardGameRunning()) return;

        try
        {
            bool installed = RuntimeInstaller.ProxyVersion(_gameDir) is not null;
            Log.Info(installed ? "Runtime remove requested." : "Runtime install requested.");
            if (installed)
            {
                RuntimeInstaller.RemoveProxy(_gameDir);
                Log.Info("Runtime removed.");
                StatusText.Text = "Runtime removed. The game is back to vanilla FOV.";
            }
            else
            {
                RuntimeInstaller.InstallProxy(_gameDir);
                Log.Info("Runtime installed: " + (RuntimeInstaller.ProxyVersion(_gameDir) ?? "unknown") + ".");
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

    private async void Dxvk_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null || _rendererBusy) return;
        if (GuardGameRunning()) return;

        if (RuntimeInstaller.DxvkInstalled(_gameDir))
        {
            try
            {
                Log.Info("DXVK remove requested.");
                RuntimeInstaller.RemoveDxvk(_gameDir);
                Log.Info("DXVK removed.");
                StatusText.Text = "DXVK removed. The game is back on D3D9.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail("Could not change DXVK", ex);
            }

            Refresh();
            return;
        }

        _rendererBusy = true;
        RefreshDxvkRow();

        string work = Path.Combine(Path.GetTempPath(), "MW3Mod");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            StatusText.Text = "Looking up the latest DXVK release...";
            var release = await DxvkDownloader.FindLatestAsync(http, CancellationToken.None);
            Log.Info("DXVK download: " + release.Tag + ".");

            string folder = Path.Combine(work, "dxvk-" + release.Tag);
            var progress = new Progress<double>(fraction =>
                StatusText.Text = $"Downloading DXVK {release.Tag}... {(int)(fraction * 100)}%");

            string dll = await DxvkDownloader.DownloadAsync(
                http, release, folder, progress, CancellationToken.None);

            RuntimeInstaller.InstallDxvk(_gameDir, dll);
            Log.Info("DXVK installed: " + release.Tag + ".");
            StatusText.Text = $"DXVK {release.Tag} installed. Start the game to render through Vulkan.";

            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or JsonException or TaskCanceledException)
        {
            Fail("Could not install DXVK", ex);
        }
        finally
        {
            _rendererBusy = false;
            Refresh();
        }
    }

    private void DxvkFile_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null || _rendererBusy) return;
        if (GuardGameRunning()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Pick DXVK's x64 d3d9.dll",
            Filter = "d3d9.dll|d3d9.dll|DLL files|*.dll",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            Log.Info("Installing DXVK from " + dialog.FileName + ".");
            RuntimeInstaller.InstallDxvk(_gameDir, dialog.FileName);
            Log.Info("DXVK installed from file.");
            StatusText.Text = "DXVK installed as d3d9.dll.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Fail("Could not install DXVK", ex);
        }

        Refresh();
    }


    private async void D3d11_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null || _rendererBusy) return;
        if (GuardGameRunning()) return;

        if (RuntimeInstaller.D3d11Installed(_gameDir))
        {
            try
            {
                Log.Info("Direct3D 11 remove requested.");
                RuntimeInstaller.RemoveD3d11(_gameDir);
                Log.Info("Direct3D 11 wrapper removed.");
                StatusText.Text = "Direct3D 11 wrapper removed. The game is back on Direct3D 9.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Fail("Could not change Direct3D 11", ex);
            }

            Refresh();
            return;
        }

        // The slot check comes before the download so a wrapper that is already
        // installed is named straight away instead of after a 9 MB fetch.
        var taken = RuntimeInstaller.Owner(_gameDir);
        if (taken != RuntimeInstaller.D3d9Owner.Nobody)
        {
            Log.Warn("Direct3D 11 blocked: d3d9.dll is owned by " + RuntimeInstaller.OwnerName(taken) + ".");
            StatusText.Text = $"d3d9.dll is taken by {RuntimeInstaller.OwnerName(taken)}. Remove it first.";
            _statusHoldUntil = Environment.TickCount64 + 4000;
            return;
        }

        _rendererBusy = true;
        RefreshD3d11Row();

        string work = Path.Combine(Path.GetTempPath(), "MW3Mod");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            StatusText.Text = "Looking up the latest dgVoodoo release...";
            var release = await DgVoodooDownloader.FindLatestAsync(http, CancellationToken.None);
            Log.Info("dgVoodoo download: " + release.Tag + ".");

            string folder = Path.Combine(work, "dgvoodoo-" + release.Tag);
            var progress = new Progress<double>(fraction =>
                StatusText.Text = $"Downloading dgVoodoo {release.Tag}... {(int)(fraction * 100)}%");

            var payload = await DgVoodooDownloader.DownloadAsync(
                http, release, folder, progress, CancellationToken.None);

            RuntimeInstaller.InstallD3d11(_gameDir, payload.Dll, payload.Conf);
            Log.Info("dgVoodoo installed: " + release.Tag + ".");
            StatusText.Text = $"dgVoodoo {release.Tag} installed. Start the game to render through Direct3D 11.";

            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or JsonException or TaskCanceledException)
        {
            Fail("Could not install Direct3D 11", ex);
        }
        finally
        {
            _rendererBusy = false;
            Refresh();
        }
    }

    private void D3d11File_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null || _rendererBusy) return;
        if (GuardGameRunning()) return;

        var dialog = new OpenFileDialog
        {
            Title = "Pick dgVoodoo's x64 D3D9.dll",
            Filter = "D3D9.dll|D3D9.dll|DLL files|*.dll",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            // When the file comes out of the zip, the conf sits at its root.
            string? folder = Path.GetDirectoryName(dialog.FileName);
            string? conf = folder is null
                ? null
                : Path.Combine(Path.GetDirectoryName(folder) ?? folder, RuntimeInstaller.DgVoodooConfName);

            Log.Info("Installing dgVoodoo from " + dialog.FileName + ".");
            RuntimeInstaller.InstallD3d11(_gameDir, dialog.FileName, conf);
            Log.Info("dgVoodoo installed from file.");
            StatusText.Text = "dgVoodoo installed as d3d9.dll.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Fail("Could not install Direct3D 11", ex);
        }

        Refresh();
    }

    private void ReShade_Click(object sender, RoutedEventArgs e)
    {
        if (_gameDir is null || _rendererBusy) return;
        if (GuardGameRunning()) return;

        try
        {
            if (RuntimeInstaller.InstalledMode(_gameDir) != RuntimeInstaller.ReShadeMode.None)
            {
                Log.Info("ReShade remove requested.");
                RuntimeInstaller.RemoveReShade(_gameDir);
                Log.Info("ReShade removed; presets and shaders kept.");
                StatusText.Text = "ReShade removed. Its presets and shaders are still in the game folder.";
            }
            else
            {
                var target = RuntimeInstaller.InstallReShade(_gameDir);
                Log.Info("ReShade installed as " + (target == RuntimeInstaller.ReShadeMode.Direct3D11 ? "dxgi.dll" : "d3d9.dll") + ".");
                StatusText.Text = target == RuntimeInstaller.ReShadeMode.Direct3D11
                    ? "ReShade installed as dxgi.dll. Start the game and press your ReShade overlay key."
                    : "ReShade installed as d3d9.dll. Start the game and press your ReShade overlay key.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or FileNotFoundException)
        {
            Fail("Could not change ReShade", ex);
        }

        Refresh();
    }

    private bool GuardGameRunning()
    {
        if (Process.GetProcessesByName("iw5sp").Length == 0) return false;

        Log.Warn("Operation blocked because iw5sp.exe is running.");
        MessageBox.Show(this,
            "Close Modern Warfare 3 first - it holds the files open while it runs.",
            "MW3 Mod Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    private void Fail(string title, Exception ex)
    {
        Log.Error(title, ex);
        StatusText.Text = ex.Message;
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e) => OpenPath(ModConfig.LogDir);

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
        string path = ModConfig.RuntimeLogPath;
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
