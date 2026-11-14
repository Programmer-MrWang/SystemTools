using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;
using SystemTools.Settings;

namespace SystemTools;

[ActionInfo("SystemTools.ScreenShot", "屏幕截图", "\uEEE7", false)]
public sealed class NativeScreenShotAction(IScreenCapture capture, FeatureCatalog catalog) : ActionBase<ScreenShotSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        if (string.IsNullOrWhiteSpace(Settings.SaveFolder)) throw new InvalidOperationException("请选择截图保存目录。");
        var directory = Path.GetFullPath(Settings.SaveFolder);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"屏幕截图{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{Guid.NewGuid():N}.png");
        var temporary = target + ".tmp";
        try
        {
            await capture.CapturePngAsync(temporary, InterruptCancellationToken);
            InterruptCancellationToken.ThrowIfCancellationRequested();
            using (var input = File.OpenRead(temporary))
            {
                Span<byte> signature = stackalloc byte[8];
                if (input.Read(signature) != 8 || !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    throw new IOException("系统没有返回有效截图。");
            }
            File.Move(temporary, target);
            NativeActionFeedback.Notify(Settings.NotifyOnExecute, "屏幕截图已保存");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
