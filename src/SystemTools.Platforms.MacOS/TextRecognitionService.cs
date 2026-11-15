using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class TextRecognitionService : ITextRecognition
{
    public Capability Probe() => NativeBridge.Probe("ocr.probe");
    public async Task<IReadOnlyList<RecognizedText>> RecognizeAsync(BackgroundFrame frame, CancellationToken token)
    {
        if (!frame.ExcludesOwnProcess || frame.Png.Length > 36_000_000 || frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
            (long)frame.PixelWidth * frame.PixelHeight > 16_000_000) throw new ArgumentException("OCR 需要经过排除自身的有效背景帧。");
        var result = await Task.Run(() => NativeBridge.Require(new { operation = "ocr.recognize", png = Convert.ToBase64String(frame.Png) }), token);
        token.ThrowIfCancellationRequested();
        return result.GetProperty("boxes").EnumerateArray().Select(box => new RecognizedText(
            new(frame.Region.X + box.GetProperty("x").GetDouble() * frame.Region.Width,
                frame.Region.Y + box.GetProperty("y").GetDouble() * frame.Region.Height,
                box.GetProperty("width").GetDouble() * frame.Region.Width,
                box.GetProperty("height").GetDouble() * frame.Region.Height), box.GetProperty("confidence").GetDouble())).ToArray();
    }
}
