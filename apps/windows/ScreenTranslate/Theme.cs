using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace ScreenTranslate;

internal static class Theme
{
    internal static readonly SolidColorBrush Window = Brush("#171B21");
    internal static readonly SolidColorBrush Surface = Brush("#222831");
    internal static readonly SolidColorBrush Text = Brush("#E8EDF2");
    internal static readonly SolidColorBrush Muted = Brush("#98A4B3");
    internal static readonly SolidColorBrush Line = Brush("#36414D");
    internal static readonly SolidColorBrush Accent = Brush("#80DAC5");
    private static SolidColorBrush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze(); return brush;
    }

    internal static void Apply(Window window)
    {
        window.Background = Window; window.Foreground = Text;
        window.SourceInitialized += (_, _) =>
        {
            var dark = 1;
            WindowNative.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
        };
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        button.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        button.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="Shell" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Shell" Property="Opacity" Value="0.85"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Shell" Property="BorderBrush" Value="#80DAC5"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Shell" Property="Opacity" Value="0.65"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        window.Resources.Add(typeof(Button), button);
        var input = new Style(typeof(TextBox));
        input.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        input.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        input.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        input.Setters.Add(new Setter(TextBox.CaretBrushProperty, Accent));
        input.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Brush("#32695F")));
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Control.BorderBrushProperty, Accent)); input.Triggers.Add(focused);
        window.Resources.Add(typeof(TextBox), input);
        var checkbox = new Style(typeof(CheckBox));
        checkbox.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        checkbox.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
              <StackPanel Orientation="Horizontal">
                <Border x:Name="Box" Width="15" Height="15" Background="#222831" BorderBrush="#647180" BorderThickness="1" CornerRadius="3" Margin="0,0,8,0">
                  <Path x:Name="Tick" Data="M 2,6 L 5,9 L 11,2" Stroke="#171B21" StrokeThickness="1.6" Visibility="Collapsed"/>
                </Border>
                <ContentPresenter VerticalAlignment="Center"/>
              </StackPanel>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="#80DAC5"/><Setter TargetName="Tick" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#80DAC5"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        window.Resources.Add(typeof(CheckBox), checkbox);
        var slider = new Style(typeof(Slider));
        slider.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
              <Grid Height="24">
                <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}">
                  <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="#80DAC5" CornerRadius="1"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb Width="14" Height="14"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#80DAC5" Stroke="#171B21" StrokeThickness="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="#36414D" CornerRadius="1"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
            """)));
        window.Resources.Add(typeof(Slider), slider);
    }
}

internal sealed class TrayColors : System.Windows.Forms.ProfessionalColorTable
{
    private static System.Drawing.Color Rgb(int r, int g, int b) => System.Drawing.Color.FromArgb(r, g, b);
    public override System.Drawing.Color ToolStripDropDownBackground => Rgb(28, 33, 41);
    public override System.Drawing.Color ImageMarginGradientBegin => ToolStripDropDownBackground;
    public override System.Drawing.Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
    public override System.Drawing.Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    public override System.Drawing.Color MenuItemSelected => Rgb(46, 62, 66);
    public override System.Drawing.Color MenuItemBorder => Rgb(72, 109, 103);
    public override System.Drawing.Color MenuBorder => Rgb(54, 65, 77);
    public override System.Drawing.Color SeparatorDark => MenuBorder;
    public override System.Drawing.Color SeparatorLight => ToolStripDropDownBackground;
    public override System.Drawing.Color CheckBackground => MenuItemSelected;
}
