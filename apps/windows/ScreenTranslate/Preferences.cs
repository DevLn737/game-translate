using System.IO;
using System.Text.Json;

namespace ScreenTranslate;

internal sealed record Preferences(string CaptureKey = "Home", string RepeatKey = "End", int Seconds = 7, bool ShowOriginal = false, string AlternativeKey = "Insert")
{
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenTranslate");
    internal static readonly string FilePath = Path.Combine(Folder, "settings.json");

    internal bool IsValid => Seconds is >= 3 and <= 20
        && ShortcutBinding.TryParse(CaptureKey, out var capture)
        && ShortcutBinding.TryParse(RepeatKey, out var repeat)
        && ShortcutBinding.TryParse(AlternativeKey, out var alternative)
        && capture != repeat && capture != alternative && repeat != alternative;

    internal static Preferences Load(string? path = null)
    {
        try
        {
            var result = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path ?? FilePath));
            return result is { IsValid: true } ? result : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    internal bool TrySave(string? path = null)
    {
        if (!IsValid) return false;
        var destination = Path.GetFullPath(path ?? FilePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination + ".tmp", JsonSerializer.Serialize(this));
            File.Move(destination + ".tmp", destination, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
