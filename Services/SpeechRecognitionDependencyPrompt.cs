using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using FluentAvalonia.UI.Controls;
using SystemTools.Shared;

namespace SystemTools.Services;

public static class SpeechRecognitionDependencyPrompt
{
    public static async Task<bool> EnsureAvailableAsync(TopLevel? owner)
    {
        var dependencyCheck = DependencyPaths.CheckSpeechRecognitionDependencies();
        if (dependencyCheck.IsAvailable)
        {
            return true;
        }

        var dialog = new FAContentDialog
        {
            Title = "需要下载语音识别服务与模型",
            Content = $"语音输入所需文件不完整，请先下载语音识别服务与模型。\n\n{dependencyCheck.Message}",
            PrimaryButtonText = "去下载",
            CloseButtonText = "取消",
            DefaultButton = FAContentDialogButton.Close
        };

        var result = owner is null
            ? await dialog.ShowAsync()
            : await dialog.ShowAsync(owner);
        if (result == FAContentDialogResult.Primary)
        {
            IAppHost.TryGetService<IUriNavigationService>()?.NavigateWrapped(
                new Uri("classisland://app/settings/systemtools.settings.main?ci_keepHistory=true"));
        }

        return false;
    }
}
