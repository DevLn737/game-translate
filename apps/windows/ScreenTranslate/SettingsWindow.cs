using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTranslate;

internal sealed class SettingsWindow : Window
{
    private readonly TextBlock _status = new() { Foreground = Theme.Accent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 24) };
    private bool _closed;
    internal void UpdateStatus(string status)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => UpdateStatus(status)); return; }
        if (!_closed) _status.Text = status;
    }

    internal SettingsWindow(Preferences prefs, string status, Func<Preferences, string?> save, Action practice)
    {
        Title = "Screen Translate · Настройки";
        Width = 430; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Apply(this); FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;
        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 24) }; Content = stack;
        stack.Children.Add(new TextBlock { Text = "Screen Translate", FontSize = 22, FontWeight = FontWeights.SemiBold });
        _status.Text = status; stack.Children.Add(_status);
        Closed += (_, _) => _closed = true;
        TextBox ShortcutRow(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Text = value, Padding = new Thickness(9, 7, 9, 7), ToolTip = "Home, End, F8 или сочетание Alt+T" };
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            Grid.SetColumn(input, 1); row.Children.Add(input); stack.Children.Add(row); return input;
        }
        var capture = ShortcutRow("Выделить текст", prefs.CaptureKey);
        var repeat = ShortcutRow("Повторить область", prefs.RepeatKey);
        var alternative = ShortcutRow("Другой перевод · Google", prefs.AlternativeKey);
        stack.Children.Add(new TextBlock { Text = "Esc отменяет выделение. Клавиши можно временно отключить в меню трея.", TextWrapping = TextWrapping.Wrap, Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 22) });
        var durationRow = new DockPanel();
        var durationLabel = new TextBlock { Text = $"{prefs.Seconds} с", Width = 38, TextAlignment = TextAlignment.Right };
        DockPanel.SetDock(durationLabel, Dock.Right); durationRow.Children.Add(durationLabel);
        durationRow.Children.Add(new TextBlock { Text = "Время показа" }); stack.Children.Add(durationRow);
        var seconds = new Slider { Minimum = 3, Maximum = 20, Value = prefs.Seconds, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 9, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(seconds, "Время показа");
        seconds.ValueChanged += (_, _) => durationLabel.Text = $"{seconds.Value:0} с"; stack.Children.Add(seconds);
        stack.Children.Add(new TextBlock { Text = "Длинные фразы остаются дольше.", Foreground = Theme.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 16) });
        var original = new CheckBox { Content = "Показывать оригинал", IsChecked = prefs.ShowOriginal, Margin = new Thickness(0, 0, 0, 18) }; stack.Children.Add(original);
        var feedback = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 164, 164)), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed }; stack.Children.Add(feedback);
        var actions = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var demo = new Button { Content = "Попробовать", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 6, 0) };
        demo.Click += (_, _) => Commit(true); actions.Children.Add(demo);
        var done = new Button { Content = "Готово", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(6, 0, 0, 0), Background = Theme.Accent, Foreground = Theme.Window, IsDefault = true };
        Grid.SetColumn(done, 1); actions.Children.Add(done); stack.Children.Add(actions);
        done.Click += (_, _) => Commit(false);
        void Commit(bool openPractice)
        {
            var next = new Preferences(capture.Text.Trim(), repeat.Text.Trim(), (int)seconds.Value, original.IsChecked == true, alternative.Text.Trim());
            var error = next.IsValid ? save(next) : "Укажите разные допустимые клавиши и время показа от 3 до 20 секунд.";
            if (error is not null) { feedback.Text = error; feedback.Visibility = Visibility.Visible; return; }
            Close();
            if (openPractice) practice();
        }
        stack.Children.Add(new TextBlock { Text = "Распознавание — на устройстве. Перевод — Яндекс, альтернатива — Google. При ошибке сервиса текст отправляется Bing. Скриншоты не сохраняются.", TextWrapping = TextWrapping.Wrap, Foreground = Theme.Muted, FontSize = 11 });
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }
}

