using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;

namespace SystemTools.Services;

[NotificationProviderInfo("7E9A3D5C-1B8F-4E2A-9C6D-0F5E8B1A4D7C", "SystemTools 通知", "\uE9FB", "来自 SystemTools 插件的提醒。")]
[NotificationChannelInfo("6F8C2B4A-9D1E-5F3B-8A7C-1E4D9F6B3A8C", "SystemTools", "\uE9FB", "SystemTools 通用通知渠道")]
public class SystemToolsNotificationProvider : NotificationProviderBase
{
}
