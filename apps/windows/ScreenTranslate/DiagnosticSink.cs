using System.IO;
using System.Text;

namespace ScreenTranslate;

internal enum DiagnosticEvent { RequestFailed, CaptureRequested, SelfTestAlreadyRunning }

// Structured events only: no OCR text, URLs, paths, screenshots or exception messages.
internal sealed class DiagnosticSink(string path, Action<string, int, byte[]>? append = null)
{
    internal const int MaximumBytes = 64 * 1024;
    internal const int MaximumEntryBytes = 256;
    private readonly object _gate = new();
    internal void Write(DiagnosticEvent code)
    {
        try
        {
            var entry = Encoding.UTF8.GetBytes($"{DateTime.UtcNow:O} {code}\n");
            if (entry.Length > MaximumEntryBytes) return;
            lock (_gate) (append ?? AppendBounded)(path, MaximumBytes, entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
    private static void AppendBounded(string path, int maximumBytes, byte[] entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (file.Length + entry.Length > maximumBytes) file.SetLength(0);
        file.Position = file.Length; file.Write(entry);
    }
}
