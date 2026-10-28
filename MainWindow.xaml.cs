using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatZ;

public partial class MainWindow : Window
{
    private record Service(string Id, string Name, string Tech, string Url);

    private static readonly Service WhatsApp = new("wa", "WhatsApp", "WebView2 · web.whatsapp.com", "https://web.whatsapp.com/");
    private static readonly Service Telegram = new("tg", "Telegram", "WebView2 · web.telegram.org", "https://web.telegram.org/a/");
    private static readonly Service Viber = new("vb", "Viber", "Ugrađen Viber Desktop prozor", "");

    private static readonly Regex UnreadRegex = new(@"\((\d+)\)", RegexOptions.Compiled);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Dictionary<string, int> _unread = new() { ["wa"] = 0, ["tg"] = 0, ["vb"] = 0 };
    private readonly DispatcherTimer _viberPoll = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _viberWatch = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _idleCheck = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly TaskbarIcon _tray;

    private Service _active = WhatsApp;
    private bool _exiting;
    private bool _startHidden;
    private bool _viberWasAttached;
    private int _viberPollTries;

    private bool _locked;
    private int _failedAttempts;
    private DateTime _lockoutUntil = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _tray = (TaskbarIcon)FindResource("TrayIcon");
        _tray.ForceCreate(false);

        _viberPoll.Tick += ViberPoll_Tick;
        _viberWatch.Tick += ViberWatch_Tick;
        _idleCheck.Tick += IdleCheck_Tick;

        Theme.Apply(_settings.Theme);
        ApplyLayout();
        RefreshThemeMenu();
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        RefreshAutostartUi();
        RefreshPinUi();
        if (_settings.PinEnabled) Lock();
        _idleCheck.Start();

        Loaded += MainWindow_Loaded;
    }

    /// <summary>Pokretanje sa --tray: prozor se napravi (da bi WebView2 i Viber radili), pa sakrije.</summary>
    public void InitializeHidden()
    {
        _startHidden = true;
        ShowActivated = false;
        WindowState = WindowState.Minimized;
        Show();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startHidden)
        {
            Hide();
            WindowState = WindowState.Normal;
            ShowActivated = true;
        }

        var start = _settings.LastService switch { "tg" => BtnTelegram, "vb" => BtnViber, _ => BtnWhatsApp };
        start.IsChecked = true;

        await InitWebAsync(WebWhatsApp, WhatsApp);
        await InitWebAsync(WebTelegram, Telegram);
        StartViber();
        _viberWatch.Start();
    }

    // ===================== Servisi i prebacivanje =====================

    private void Service_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        _active = (rb.Tag as string) switch { "tg" => Telegram, "vb" => Viber, _ => WhatsApp };

        WebWhatsApp.Visibility = _active == WhatsApp ? Visibility.Visible : Visibility.Hidden;
        WebTelegram.Visibility = _active == Telegram ? Visibility.Visible : Visibility.Hidden;
        ViberPane.Visibility = _active == Viber ? Visibility.Visible : Visibility.Hidden;

        HeaderName.Text = _active.Name;
        HeaderTech.Text = _active.Tech;

        if (!_locked)
        {
            if (_active == Viber) ViberHostControl.FocusViber();
            else (_active == WhatsApp ? WebWhatsApp : WebTelegram).Focus();
        }

        _settings.LastService = _active.Id;
        _settings.Save();
        UpdateTitles();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_locked) return;

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L && _settings.PinEnabled)
        {
            Lock();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            RadioButton? target = e.Key switch
            {
                Key.D1 or Key.NumPad1 => BtnWhatsApp,
                Key.D2 or Key.NumPad2 => BtnTelegram,
                Key.D3 or Key.NumPad3 => BtnViber,
                _ => null
            };
            if (target != null) { target.IsChecked = true; e.Handled = true; }
        }
        else if (e.Key == Key.F5)
        {
            ReloadActive();
            e.Handled = true;
        }
    }

    // ===================== WebView2 (WhatsApp, Telegram) =====================

    private async Task InitWebAsync(WebView2 view, Service service)
    {
        try
        {
            string folder = Path.Combine(AppSettings.DataDir, "WebView2", service.Id);
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await view.EnsureCoreWebView2Async(env);

            var core = view.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;

            core.PermissionRequested += (_, args) =>
            {
                if (args.PermissionKind == CoreWebView2PermissionKind.Notifications)
                    args.State = CoreWebView2PermissionState.Allow;
            };

            // Linkovi iz poruka se otvaraju u običnom pregledaču.
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenExternal(args.Uri);
            };

            core.DocumentTitleChanged += (_, _) => SetUnread(service.Id, ParseUnread(core.DocumentTitle));

            core.Navigate(service.Url);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Na računaru nije instaliran Microsoft Edge WebView2 Runtime.\n\n" +
                "Preuzmi ga sa: https://developer.microsoft.com/microsoft-edge/webview2/",
                "ChatZ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{service.Name} nije mogao da se učita:\n{ex.Message}",
                "ChatZ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static int ParseUnread(string title)
    {
        var m = UnreadRegex.Match(title ?? "");
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
    }

    private static void OpenExternal(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    private void ReloadActive()
    {
        if (_active == WhatsApp) WebWhatsApp.CoreWebView2?.Reload();
        else if (_active == Telegram) WebTelegram.CoreWebView2?.Reload();
        else StartViber();
    }

    // ===================== Nepročitane poruke =====================

    private void SetUnread(string id, int count)
    {
        _unread[id] = count;

        var (badge, text) = id switch
        {
            "wa" => (BadgeWhatsApp, BadgeWhatsAppText),
            "tg" => (BadgeTelegram, BadgeTelegramText),
            _ => (BadgeViber, BadgeViberText),
        };
        badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        text.Text = count > 99 ? "99+" : count.ToString();

        UpdateTitles();
    }

    private void UpdateTitles()
    {
        int total = _unread.Values.Sum();
        string prefix = total > 0 ? $"({total}) " : "";

        Title = $"{prefix}ChatZ — {_active.Name}";
        StatusUnread.Text = $"Ukupno nepročitano: {total}";
        _tray.ToolTipText = total > 0 ? $"ChatZ — {total} nepročitanih" : "ChatZ";
        TaskbarInfo.Overlay = total > 0 ? CreateBadgeImage(total) : null;
    }

    private ImageSource CreateBadgeImage(int count)
    {
        const int size = 32;
        string label = count > 99 ? "99+" : count.ToString();
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)), null,
                new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);

            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                label.Length > 2 ? 11 : 16, Brushes.White, pixelsPerDip);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    // ===================== Viber =====================

    private void StartViber()
    {
        _viberPoll.Stop();
        ViberHostControl.Detach();
        ShowViberStatus("Pokrećem Viber…",
            "ChatZ pokreće instalirani Viber Desktop i ugrađuje njegov prozor ovde.", retry: false, browse: false);

        if (!ViberLocator.IsRunning())
        {
            string? exe = ViberLocator.FindExe(_settings.ViberPath);
            if (exe == null)
            {
                ShowViberStatus("Viber Desktop nije pronađen",
                    "Instaliraj Viber za Windows ili pokaži gde se nalazi Viber.exe.", retry: true, browse: true);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowViberStatus("Viber nije mogao da se pokrene", ex.Message, retry: true, browse: true);
                return;
            }
        }

        _viberPollTries = 0;
        _viberPoll.Start();
    }

    private void ViberPoll_Tick(object? sender, EventArgs e)
    {
        _viberPollTries++;
        IntPtr hwnd = ViberLocator.FindMainWindow();

        if (hwnd != IntPtr.Zero && ViberHostControl.Attach(hwnd))
        {
            _viberPoll.Stop();
            _viberWasAttached = true;
            ViberPlaceholder.Visibility = Visibility.Collapsed;
            ViberHostControl.Visibility = Visibility.Visible;
            return;
        }

        if (_viberPollTries >= 60) // ~30 sekundi
        {
            _viberPoll.Stop();
            ShowViberStatus("Nisam pronašao Viber prozor",
                "Viber možda još nije podešen. Otvori ga jednom ručno, prijavi se, pa klikni „Pokreni ponovo“.",
                retry: true, browse: false);
        }
    }

    private void ViberWatch_Tick(object? sender, EventArgs e)
    {
        if (_viberWasAttached && !ViberHostControl.IsAttached)
        {
            _viberWasAttached = false;
            ViberHostControl.Detach();
            ShowViberStatus("Viber je zatvoren",
                "Viber Desktop je ugašen ili se restartovao (npr. posle update-a).", retry: true, browse: false);
            SetUnread("vb", 0);
        }
    }

    private async void ViberDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        string baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "ChatZ-viber-dijagnostika");
        IntPtr hwnd = ViberHostControl.IsAttached ? ViberHostControl.ViberWindow : IntPtr.Zero;

        string folder;
        Cursor = Cursors.Wait;
        try
        {
            folder = await Task.Run(() => ViberDiagnostics.Write(baseFolder, hwnd));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ChatZ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            Cursor = null;
        }

        MessageBox.Show(this,
            "Snimljeno u folder ChatZ-viber-dijagnostika na Desktopu.\n\n" +
            "Snimi dva puta: jednom dok imaš nepročitane Viber poruke, i jednom kad ih sve pročitaš. " +
            "Onda ceo folder spakuj u zip (desni klik → Pošalji u → Kompresovana fascikla) i pošalji ga.",
            "ChatZ", MessageBoxButton.OK, MessageBoxImage.Information);
        try { Process.Start("explorer.exe", $"\"{baseFolder}\""); } catch { }
    }

    // ===================== Tema i raspored =====================

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string mode })
        {
            _settings.Theme = mode;
            _settings.Save();
            Theme.Apply(mode);
        }
        RefreshThemeMenu();
    }

    private void RefreshThemeMenu()
    {
        MenuThemeSystem.IsChecked = _settings.Theme == "system";
        MenuThemeLight.IsChecked = _settings.Theme == "light";
        MenuThemeDark.IsChecked = _settings.Theme == "dark";
        MenuShowHeader.IsChecked = _settings.ShowHeader;
        MenuShowFooter.IsChecked = _settings.ShowFooter;
    }

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowHeader = MenuShowHeader.IsChecked;
        _settings.ShowFooter = MenuShowFooter.IsChecked;
        _settings.Save();
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        HeaderBar.Visibility = _settings.ShowHeader ? Visibility.Visible : Visibility.Collapsed;
        ContentBorder.BorderThickness = new Thickness(0, _settings.ShowHeader ? 1 : 0, 0, 0);
        FooterBar.Visibility = _settings.ShowFooter ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (_settings.Theme == "system") Theme.Apply("system");
        });
    }

    // ===================== PIN zaštita =====================

    private void Lock()
    {
        if (!_settings.PinEnabled || _locked) return;

        _locked = true;
        ContentRoot.Visibility = Visibility.Hidden; // sakriva i WebView2 i Viber prozor
        LockPanel.Visibility = Visibility.Visible;
        LockPassword.Clear();
        LockError.Visibility = Visibility.Collapsed;
        if (IsVisible && IsActive) LockPassword.Focus();
    }

    private void TryUnlock()
    {
        if (DateTime.Now < _lockoutUntil)
        {
            int secs = (int)Math.Ceiling((_lockoutUntil - DateTime.Now).TotalSeconds);
            ShowLockError($"Previše pogrešnih pokušaja. Sačekaj još {secs} s.");
            LockPassword.Clear();
            return;
        }

        if (_settings.VerifyPin(LockPassword.Password))
        {
            _locked = false;
            _failedAttempts = 0;
            LockPassword.Clear();
            LockPanel.Visibility = Visibility.Collapsed;
            ContentRoot.Visibility = Visibility.Visible;

            if (_active == Viber) ViberHostControl.FocusViber();
            else (_active == WhatsApp ? WebWhatsApp : WebTelegram).Focus();
            return;
        }

        LockPassword.Clear();
        if (++_failedAttempts >= 5)
        {
            _failedAttempts = 0;
            _lockoutUntil = DateTime.Now.AddSeconds(30);
            ShowLockError("Previše pogrešnih pokušaja. Sačekaj 30 sekundi.");
        }
        else
        {
            ShowLockError("Pogrešan PIN.");
        }
    }

    private void ShowLockError(string text)
    {
        LockError.Text = text;
        LockError.Visibility = Visibility.Visible;
    }

    private void LockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryUnlock();
            e.Handled = true;
        }
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    private void LockNow_Click(object sender, RoutedEventArgs e) => Lock();

    private void PinSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_locked) return;
        new PinDialog(_settings) { Owner = this }.ShowDialog();
        RefreshPinUi();
    }

    private void RefreshPinUi()
    {
        bool on = _settings.PinEnabled;
        LockButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        MenuLockNow.IsEnabled = on;
    }

    private void IdleCheck_Tick(object? sender, EventArgs e)
    {
        if (_locked || !_settings.PinEnabled || _settings.AutoLockMinutes <= 0) return;
        if (Native.IdleTime() >= TimeSpan.FromMinutes(_settings.AutoLockMinutes)) Lock();
    }

    private void ShowViberStatus(string title, string text, bool retry, bool browse)
    {
        ViberHostControl.Visibility = Visibility.Hidden;
        ViberPlaceholder.Visibility = Visibility.Visible;
        ViberStatusTitle.Text = title;
        ViberStatusText.Text = text;
        ViberRetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        ViberBrowseButton.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViberReconnect_Click(object sender, RoutedEventArgs e) => StartViber();

    private void ViberBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Izaberi Viber.exe",
            Filter = "Viber|Viber.exe|Programi (*.exe)|*.exe",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _settings.ViberPath = dlg.FileName;
            _settings.Save();
            StartViber();
        }
    }

    // ===================== Podešavanja, tray, zatvaranje =====================

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        RefreshAutostartUi();
        var menu = SettingsButton.ContextMenu;
        menu.PlacementTarget = SettingsButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        menu.IsOpen = true;
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Autostart.Set(MenuAutostart.IsChecked); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ChatZ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshAutostartUi();
    }

    private void RefreshAutostartUi()
    {
        bool on = Autostart.IsEnabled();
        MenuAutostart.IsChecked = on;
        StatusAutostart.Text = $"Pokretanje sa Windowsom: {(on ? "uključeno" : "isključeno")}";
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => ReloadActive();

    private void Tray_LeftMouseUp(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (_locked) LockPassword.Focus();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            // X samo sklanja prozor u tray.
            e.Cancel = true;
            Lock();
            Hide();
            return;
        }

        _viberPoll.Stop();
        _viberWatch.Stop();
        _idleCheck.Stop();
        ViberHostControl.Detach(); // Viber nastavlja da radi u svom trayu
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _tray.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
