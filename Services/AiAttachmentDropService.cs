using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SystemTools.Controls;
using SystemTools.Models;

namespace SystemTools.Services;

public static class AiAttachmentDropService
{
    public static async Task<AiAttachmentLoadResult?> LoadAndConfirmAsync(
        TopLevel owner,
        IReadOnlyList<IStorageFile> files,
        int existingCount,
        long existingBytes)
    {
        var result = await AiAttachmentService.LoadFilesAsync(
            files,
            existingCount,
            existingBytes);
        if (result.Accepted.Count == 0)
        {
            return result;
        }

        try
        {
            var dialog = new FAContentDialog
            {
                Title = result.Accepted.Count == 1 ? "确认上传此附件？" : $"确认上传这 {result.Accepted.Count} 个附件？",
                Content = new AiAttachmentDropConfirmation(result),
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                DefaultButton = FAContentDialogButton.Primary
            };

            if (await dialog.ShowAsync(owner) == FAContentDialogResult.Primary)
            {
                return result;
            }
        }
        catch
        {
            DisposeAll(result.Accepted);
            throw;
        }

        DisposeAll(result.Accepted);
        return null;
    }

    private static void DisposeAll(IEnumerable<AiAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            attachment.Dispose();
        }
    }
}
