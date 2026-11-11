using System.Drawing;

namespace ScreenTranslate.Capture.Ocr;

internal enum OcrOutcome { Success, NoText, PackageIdentityMissing, EnglishOcrUnavailable, UnexpectedError }

internal sealed record OcrWord(string Text, RectangleF ScreenBounds);
internal sealed record OcrLine(IReadOnlyList<OcrWord> Words);
internal sealed record OcrResult(OcrOutcome Outcome, IReadOnlyList<OcrWord> Words, IReadOnlyList<OcrLine> Lines, string Message)
{
    internal static OcrResult Failure(OcrOutcome outcome, string message) => new(outcome, [], [], message);
}

internal enum OcrAttemptStage { SelectingRegion, Recognizing, SelectingWord, Completed, Cancelled }

internal sealed class OcrAttempt
{
    internal OcrAttemptStage Stage { get; private set; } = OcrAttemptStage.SelectingRegion;
    internal bool BeginRecognition() { if (Stage != OcrAttemptStage.SelectingRegion) return false; Stage = OcrAttemptStage.Recognizing; return true; }
    internal bool Accept() { if (Stage != OcrAttemptStage.Recognizing) return false; Stage = OcrAttemptStage.SelectingWord; return true; }
    internal void Reselect() { if (Stage is OcrAttemptStage.Recognizing or OcrAttemptStage.SelectingWord) Stage = OcrAttemptStage.SelectingRegion; }
    internal void Complete() => Stage = OcrAttemptStage.Completed;
    internal void Cancel() => Stage = OcrAttemptStage.Cancelled;
}

internal static class OcrText
{
    internal static string Term(string text)
    {
        var value = text.Trim();
        var start = 0; var end = value.Length;
        while (start < end && (char.IsPunctuation(value[start]) || char.IsWhiteSpace(value[start]))) start++;
        while (end > start && (char.IsPunctuation(value[end - 1]) || char.IsWhiteSpace(value[end - 1]))) end--;
        return value[start..end];
    }

    internal static string Context(OcrResult result) => string.Join(Environment.NewLine,
        result.Lines.Select(line => string.Join(" ", line.Words.Select(word => word.Text))));
}


