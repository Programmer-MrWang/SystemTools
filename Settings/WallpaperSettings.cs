using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public enum ChangeWallpaperMode
{
    Image = 0,
    SolidColor = 1
}

public class ChangeWallpaperSettings
{
    [JsonPropertyName("imagePath")] public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("mode")] public ChangeWallpaperMode Mode { get; set; } = ChangeWallpaperMode.Image;

    [JsonPropertyName("solidColor")] public string SolidColor { get; set; } = "#000000";

    // 壁纸契合度：0=平铺,1=居中,2=拉伸,3=填充,4=适应,5=跨区
    [JsonPropertyName("fitStyle")]
    public int FitStyle { get; set; } = 3; // 默认填充
}
