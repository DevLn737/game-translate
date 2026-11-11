using Windows.ApplicationModel;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace ScreenTranslate.Capture.Ocr;

internal sealed class SystemOcrRunner
{
    private OcrEngine? _engine;
    internal uint MaxImageDimension => OcrEngine.MaxImageDimension;
    internal OcrResult Initialize()
    {
        try { _ = Package.Current.Id.Name; }
        catch { return OcrResult.Failure(OcrOutcome.PackageIdentityMissing, "OCR requires the packaged Screen Translate app."); }
        var language = new Language("en-US");
        if (!OcrEngine.IsLanguageSupported(language)) return OcrResult.Failure(OcrOutcome.EnglishOcrUnavailable, "Install English OCR in Windows language options.");
        _engine = OcrEngine.TryCreateFromLanguage(language);
        return _engine is null ? OcrResult.Failure(OcrOutcome.EnglishOcrUnavailable, "English OCR is unavailable.") : new(OcrOutcome.Success, [], [], "Ready");
    }
    internal async Task<OcrResult> RecognizeAsync(CapturedFrame frame)
    {
        if (_engine is null) return OcrResult.Failure(OcrOutcome.EnglishOcrUnavailable, "English OCR is not initialized.");
        using var bitmap = frame.CreateSoftwareBitmap();
        var result = await _engine.RecognizeAsync(bitmap);
        var origin = frame.Region.ScreenBounds.Location;
        var lines = result.Lines.Select(line => new OcrLine(line.Words.Select(word => new OcrWord(word.Text,
            new System.Drawing.RectangleF(origin.X + (float)word.BoundingRect.X, origin.Y + (float)word.BoundingRect.Y, (float)word.BoundingRect.Width, (float)word.BoundingRect.Height))).ToArray())).Where(line => line.Words.Count > 0).ToArray();
        var words = lines.SelectMany(line => line.Words).ToArray();
        return new(words.Length == 0 ? OcrOutcome.NoText : OcrOutcome.Success, words, lines, words.Length == 0 ? "Text not found" : "Ready");
    }
}
