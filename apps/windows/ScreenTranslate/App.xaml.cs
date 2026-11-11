using System.IO;
using System.Windows;
using System.Windows.Interop;
using ScreenTranslate.Capture;
using ScreenTranslate.Capture.Ocr;
using RectI = System.Drawing.Rectangle;

namespace ScreenTranslate;

public partial class App : System.Windows.Application
{
    private readonly SystemOcrRunner _ocr = new();
    private readonly GameTranslator _translator = new();
    private Preferences _prefs = Preferences.Load();
    private System.Windows.Forms.NotifyIcon? _tray;
    private GlobalHotkey? _captureKey, _repeatKey, _alternativeKey;
    private Window _host = null!;
    private RequestCoordinator? _coordinator;
    private readonly DiagnosticSink _diagnosticSink = new(Path.Combine(Preferences.Folder, "diagnostics.txt"));
    private SettingsWindow? _settings;
    private string _status = "OCR запускается…";
    private bool _ready;
    private bool _diagnostics;
    private bool _paused;
    private Mutex? _instance;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _activationWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // This utility only renders a static screenshot and a small text card.
        // Keep its WPF surfaces off the game's hardware rendering path.
        System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);
        _diagnostics = e.Args.Contains("--diagnostics");
        _instance = new Mutex(true, "Local\\DevLn737.ScreenTranslate", out var created);
        var startup = StartupPolicy.Decide(e.Args, created);
        if (!startup.Continue)
        {
            if (startup.ActivateSettings)
            {
                try { using var signal = EventWaitHandle.OpenExisting("Local\\DevLn737.ScreenTranslate.Activate"); signal.Set(); }
                catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException) { }
            }
            // A rejected self-test must not activate settings or reuse an old result.
            if (startup.ExitCode == 2) Trace(DiagnosticEvent.SelfTestAlreadyRunning);
            Shutdown(startup.ExitCode); return;
        }
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DevLn737.ScreenTranslate.Activate");
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(ShowSettings);
        }, null, Timeout.Infinite, false);
        _host = new Window { ShowInTaskbar = false, Width = 0, Height = 0, WindowStyle = WindowStyle.None };
        new WindowInteropHelper(_host).EnsureHandle();
        _coordinator = new RequestCoordinator(new WindowsRequestPlatform(_ocr, _translator,
            () => new WindowInteropHelper(_host).Handle, () => _diagnostics, Notify, _ => Trace(DiagnosticEvent.RequestFailed)), () => _prefs);
        CreateHotkeys();
        var registration = Apply(_prefs);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayColors());
        menu.ForeColor = System.Drawing.Color.FromArgb(232, 237, 242);
        menu.Font = new System.Drawing.Font("Segoe UI", 10);
        var selectItem = menu.Items.Add("Выделить текст", null, async (_, _) => { await Task.Delay(200); await CaptureAsync(false); });
        var repeatItem = menu.Items.Add("Повторить область", null, async (_, _) => { await Task.Delay(200); await CaptureAsync(true); });
        var alternativeItem = menu.Items.Add("Другой перевод · Google", null, async (_, _) => { await Task.Delay(200); await AlternativeAsync(); });
        var copyItem = menu.Items.Add("Копировать перевод", null, (_, _) =>
        {
            if (_coordinator?.LastTranslation is not { } translation) return;
            try { System.Windows.Clipboard.SetText(translation); } catch { Notify("Буфер обмена занят. Повторите попытку."); }
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var pauseItem = new System.Windows.Forms.ToolStripMenuItem("Пауза горячих клавиш");
        pauseItem.Click += (_, _) => TogglePause(); menu.Items.Add(pauseItem);
        menu.Items.Add("Настройки", null, (_, _) => ShowSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Shutdown());
        menu.Opening += (_, _) =>
        {
            selectItem.Text = $"Выделить текст\t{_prefs.CaptureKey}";
            repeatItem.Text = $"Повторить область\t{_prefs.RepeatKey}";
            repeatItem.Enabled = _coordinator?.Anchor is not null;
            alternativeItem.Text = $"Другой перевод · Google\t{_prefs.AlternativeKey}";
            alternativeItem.Enabled = _coordinator?.LastText is not null;
            copyItem.Enabled = _coordinator?.LastTranslation is not null;
            pauseItem.Checked = _paused;
        };
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
        _tray = new System.Windows.Forms.NotifyIcon { Text = "Screen Translate", Icon = new System.Drawing.Icon(iconStream), ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();
        try
        {
            var result = await Task.Run(_ocr.Initialize);
            _ready = result.Outcome == OcrOutcome.Success;
            _status = _ready ? "Готово · английский → русский" : result.Outcome == OcrOutcome.PackageIdentityMissing
                ? "Запустите Screen Translate через меню Пуск после установки пакета."
                : "Установите компонент OCR английского языка в параметрах языка Windows.";
        }
        catch { _status = "Не удалось запустить Windows OCR. Перезапустите приложение."; }
        _settings?.UpdateStatus(_status);
        if (startup.SelfTest) { Shutdown(await SelfTestAsync()); return; }
        if (startup.Practice) ShowPractice();
        if (!File.Exists(Preferences.FilePath))
        {
            Notify($"{_prefs.CaptureKey} — выделить текст · {_prefs.RepeatKey} — повторить область. Настройки — в трее. Для игры используйте режим без рамки.");
            if (!_prefs.TrySave()) Notify("Не удалось сохранить настройки. В этом сеансе используются настройки в памяти.");
        }
        if (!_ready) Notify(_status);
        if (registration is not null) Notify(registration);
    }

    private void CreateHotkeys()
    {
        var handle = new WindowInteropHelper(_host).Handle;
        _captureKey = new GlobalHotkey(handle, 0x5354); _repeatKey = new GlobalHotkey(handle, 0x5355);
        _captureKey.Triggered += async (_, _) => await CaptureAsync(false);
        _repeatKey.Triggered += async (_, _) => await CaptureAsync(true);
        _alternativeKey = new GlobalHotkey(handle, 0x5357);
        _alternativeKey.Triggered += async (_, _) => await AlternativeAsync();
    }

    private void TogglePause()
    {
        Cancel();
        if (!_paused)
        {
            _captureKey?.Dispose(); _repeatKey?.Dispose(); _alternativeKey?.Dispose(); _paused = true;
        }
        else
        {
            _paused = false; CreateHotkeys();
            var error = Apply(_prefs);
            if (error is not null) { _captureKey?.Dispose(); _repeatKey?.Dispose(); _alternativeKey?.Dispose(); _paused = true; Notify(error); }
        }
        if (_tray is not null) _tray.Text = _paused ? "Screen Translate · пауза" : "Screen Translate";
    }

    private string? Apply(Preferences next)
    {
        if (!ShortcutBinding.TryParse(next.CaptureKey, out var capture) || !ShortcutBinding.TryParse(next.RepeatKey, out var repeat) || capture == repeat || !ShortcutBinding.TryParse(next.AlternativeKey, out var alternative) || alternative == capture || alternative == repeat)
            return "Укажите разные клавиши (Home / End / F8) или сочетания с Alt, Ctrl, Shift. F12 зарезервирована Windows.";
        if (_paused) return null;
        // Release all first so swapping Home and End is also a valid update.
        _captureKey?.Dispose(); _repeatKey?.Dispose(); _alternativeKey?.Dispose(); CreateHotkeys();
        var captureRegistered = _captureKey!.TryRegister(capture!);
        if (!captureRegistered || !_repeatKey!.TryRegister(repeat!) || !_alternativeKey!.TryRegister(alternative!))
        {
            _captureKey.Dispose(); _repeatKey!.Dispose(); _alternativeKey?.Dispose(); CreateHotkeys();
            var restoredCapture = ShortcutBinding.TryParse(_prefs.CaptureKey, out var previous) && _captureKey!.TryRegister(previous!);
            var restoredRepeat = ShortcutBinding.TryParse(_prefs.RepeatKey, out var previousRepeat) && _repeatKey!.TryRegister(previousRepeat!);
            var restoredAlternative = ShortcutBinding.TryParse(_prefs.AlternativeKey, out var previousAlternative) && _alternativeKey!.TryRegister(previousAlternative!);
            var error = captureRegistered ? "Клавиша повтора или альтернативного перевода занята другим приложением." : "Клавиша выделения занята другим приложением.";
            return restoredCapture && restoredRepeat && restoredAlternative ? error : error + " Не удалось восстановить прежние горячие клавиши. Задайте свободные сочетания.";
        }
        return null;
    }

    private void ShowSettings()
    {
        if (_settings is not null) { _settings.UpdateStatus(_status); _settings.Activate(); return; }
        _settings = new SettingsWindow(_prefs, _status, next =>
        {
            var error = SettingsSavePolicy.Save(next, _prefs, Apply, value => value.TrySave());
            if (error is null) _prefs = next;
            return error;
        }, ShowPractice);
        _settings.Closed += (_, _) => _settings = null; _settings.Show();
    }

    private void ShowPractice()
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(35) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"Нажмите {_prefs.CaptureKey} и выделите английскую фразу.", FontSize = 16, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "The treasure is behind the door.", FontSize = 30, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 35, 0, 25) });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"Затем попробуйте {_prefs.RepeatKey}: та же область переводится без выделения.", FontSize = 14, TextWrapping = TextWrapping.Wrap });
        var practice = new Window { Title = "Screen Translate — тренировка", Width = 720, Height = 310, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        Theme.Apply(practice); practice.Show();
    }

    private void Notify(string text) => _tray?.ShowBalloonTip(4000, "Screen Translate", text, System.Windows.Forms.ToolTipIcon.Info);
    private void Cancel() => _coordinator?.Cancel();
    private Task CaptureAsync(bool repeat)
    {
        Trace(DiagnosticEvent.CaptureRequested);
        if (!_ready) { Cancel(); Notify(_status); return Task.CompletedTask; }
        return _coordinator?.CaptureAsync(repeat) ?? Task.CompletedTask;
    }
    private Task AlternativeAsync() => _coordinator?.AlternativeAsync() ?? Task.CompletedTask;
    private void Trace(DiagnosticEvent code) { if (_diagnostics) _diagnosticSink.Write(code); }

    private async Task<int> SelfTestAsync()
    {
        try
        {
            Directory.CreateDirectory(Preferences.Folder);
            if (!_ready) throw new InvalidOperationException(_status);
            using var bitmap = new System.Drawing.Bitmap(900, 150);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font("Segoe UI", 32))
            { graphics.Clear(System.Drawing.Color.White); graphics.DrawString("The treasure is behind the door.", font, System.Drawing.Brushes.Black, 15, 35); }
            using var frame = new CapturedFrame((System.Drawing.Bitmap)bitmap.Clone(), new CaptureRegion(new RectI(0, 0, 900, 150)));
            var result = await _ocr.RecognizeAsync(frame);
            var text = OcrText.Context(result);
            if (!text.Contains("treasure", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("OCR: " + text);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var translated = await _translator.TranslateAsync(text, false, timeout.Token);
            var alternative = await _translator.TranslateAsync(text, true, timeout.Token);
            File.WriteAllText(Path.Combine(Preferences.Folder, "self-test.txt"), $"PASS\nOCR: {text}\nTranslation ({translated.Provider}): {translated.Text}\nAlternative ({alternative.Provider}): {alternative.Text}");
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Preferences.Folder, "self-test.txt"), "FAIL" + Environment.NewLine + ex); }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
            return 1;
        }
        return 0;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _coordinator?.Dispose(); _captureKey?.Dispose(); _repeatKey?.Dispose(); _alternativeKey?.Dispose(); _tray?.Dispose();
        _activationWait?.Unregister(null); _activation?.Dispose();
        _translator.Dispose(); _instance?.Dispose(); base.OnExit(e);
    }
}

