using System.Windows.Input;
using ScreenTranslate;
using ScreenTranslate.Capture;
using Xunit;

public class HotkeyValidationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("22")]
    [InlineData("+22")]
    [InlineData("-1")]
    [InlineData("999999")]
    [InlineData("System")]
    [InlineData("ImeProcessed")]
    [InlineData("DeadCharProcessed")]
    [InlineData("None")]
    [InlineData("F12")]
    [InlineData("LeftCtrl")]
    [InlineData("RightAlt")]
    [InlineData("LWin")]
    [InlineData("NotAKey")]
    [InlineData("Home, End")]
    public void InvalidTokensAreRejectedByBothPaths(string token)
    {
        Assert.False(HotkeyBinding.TryParse("Ctrl+" + token, out var modified));
        Assert.Null(modified);
        Assert.False(ShortcutBinding.TryParse("Ctrl+" + token, out var shortcut));
        Assert.Null(shortcut);
        Assert.False(ShortcutBinding.TryParse(token, out var bare));
        Assert.Null(bare);
    }

    [Fact]
    public void NumericValuesOfEveryNamedKeyAreNeverConfigurationTokens()
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            var token = ((int)key).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.False(HotkeyBinding.TryParse("Alt+" + token, out _));
            Assert.False(ShortcutBinding.TryParse("Alt+" + token, out _));
            Assert.False(ShortcutBinding.TryParse(token, out _));
        }
    }

    [Theory]
    [InlineData("Ctrl+T", Key.T)]
    [InlineData("control+shift+home", Key.Home)]
    [InlineData("Alt+F11", Key.F11)]
    [InlineData("Ctrl+OemPlus", Key.OemPlus)]
    [InlineData("Shift+D1", Key.D1)]
    public void SupportedCombinationsRemainValid(string text, Key key)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var modified));
        Assert.True(ShortcutBinding.TryParse(text, out var shortcut));
        Assert.Equal(modified, shortcut);
        Assert.Equal(key, shortcut!.Key);
        Assert.True(shortcut.Modifiers.HasFlag(HotkeyModifiers.NoRepeat));
        Assert.InRange(KeyInterop.VirtualKeyFromKey(key), 1, 254);
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("End")]
    [InlineData("Insert")]
    [InlineData("Pause")]
    [InlineData("Scroll")]
    [InlineData("F1")]
    [InlineData("F11")]
    public void SupportedBareKeysRemainValidOnlyInShortcutPath(string text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out _));
        Assert.True(ShortcutBinding.TryParse(text, out var binding));
        Assert.Equal(HotkeyModifiers.NoRepeat, binding!.Modifiers);
        Assert.InRange(KeyInterop.VirtualKeyFromKey(binding.Key), 1, 254);
    }

    [Theory]
    [InlineData("T")]
    [InlineData("Space")]
    [InlineData("D1")]
    public void OrdinaryTypingCannotBeBoundWithoutModifiers(string text)
        => Assert.False(ShortcutBinding.TryParse(text, out _));
}
