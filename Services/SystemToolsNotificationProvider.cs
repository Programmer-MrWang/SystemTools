using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using System;
using SystemTools.Controls.Notifications;

namespace SystemTools.Services;

[NotificationProviderInfo("7E9A3D5C-1B8F-4E2A-9C6D-0F5E8B1A4D7C", "SystemTools 通知", "\uE9FB", "来自 SystemTools 插件的提醒。")]
[NotificationChannelInfo("6F8C2B4A-9D1E-5F3B-8A7C-1E4D9F6B3A8C", "SystemTools", "\uE9FB", "SystemTools 通用通知渠道")]
[NotificationChannelInfo(AiReplyChannelId, "AI 回复通知", "\uEFFF", "AI 回复完成时显示回复内容。")]
public class SystemToolsNotificationProvider : NotificationProviderBase
{
    public const string AiReplyChannelId = "7D7EFBF1-02A4-4A15-9C1A-2229027339B2";

    public void ShowAiReplyNotification(string reply)
    {
        var notificationText = NormalizeAiReply(reply);
        if (notificationText.Length == 0)
        {
            return;
        }

        var request = new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask("有新的AI回复…", factory: content =>
            {
                content.Duration = TimeSpan.FromSeconds(1);
                content.IsSpeechEnabled = false;
            }),
            OverlayContent = new NotificationContent(new AiReplyNotificationContent(notificationText))
            {
                Duration = AiReplyNotificationContent.EstimateDisplayDuration(notificationText),
                SpeechContent = notificationText,
                IsSpeechEnabled = false
            },
            ChannelId = Guid.Parse(AiReplyChannelId)
        };

        Channel(AiReplyChannelId).ShowNotification(request);
    }

    internal static string NormalizeAiReply(string reply)
    {
        return string.Join(
                " ",
                (reply ?? string.Empty)
                .Replace("#", string.Empty, StringComparison.Ordinal)
                .Replace("*", string.Empty, StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }
}
