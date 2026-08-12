using ClassIsland.Core;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.Shared;

namespace SystemTools.Services;

public sealed class AboutTitleImageCacheService(ILogger<AboutTitleImageCacheService> logger)
{
    private const string DownloadUrl =
        "https://livefile.xesimg.com/programme/python_assets/ef14f9e238d9fd955f0054e1679ba589.png";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public event EventHandler<string>? ImagePathChanged;

    public static string CachePath => Path.GetFullPath(
        Path.Combine(CommonDirectories.AppCacheFolderPath, "SystemTools", "title.png"));

    public static string PluginPath => Path.GetFullPath(
        Path.Combine(GlobalConstants.Information.PluginFolder, "title.png"));

    public string CurrentImagePath => File.Exists(CachePath) ? CachePath : PluginPath;

    public void Start() => _ = EnsureImageAsync();

    private async Task EnsureImageAsync()
    {
        await _downloadLock.WaitAsync();
        var temporaryPath = CachePath + ".download";
        try
        {
            logger.LogInformation("正在检查关于页顶部图像缓存：{ImagePath}", CachePath);
            if (File.Exists(CachePath))
            {
                logger.LogInformation("关于页顶部图像缓存已存在，跳过后台下载。");
                return;
            }

            if (!File.Exists(PluginPath))
            {
                logger.LogWarning("未找到关于页顶部图像的插件内置文件：{ImagePath}", PluginPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            logger.LogInformation("关于页顶部图像缓存不存在，当前使用 {FallbackPath}，开始从 {DownloadUrl} 后台下载。",
                PluginPath,
                DownloadUrl);

            using var response = await HttpClient.GetAsync(
                DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous))
            {
                await response.Content.CopyToAsync(output);
            }

            await ValidatePngAsync(temporaryPath);
            File.Move(temporaryPath, CachePath, overwrite: true);
            logger.LogInformation("关于页顶部图像后台下载完成：{ImagePath}", CachePath);
            ImagePathChanged?.Invoke(this, CachePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "关于页顶部图像后台下载失败，将继续使用插件内置图像并在下次启动时重试。");
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to delete the temporary about page title image.");
            }

            _downloadLock.Release();
        }
    }

    private static async Task ValidatePngAsync(string path)
    {
        var signature = new byte[PngSignature.Length];
        await using var input = File.OpenRead(path);
        var bytesRead = await input.ReadAsync(signature);
        if (bytesRead != PngSignature.Length || !signature.AsSpan().SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("The downloaded about page title image is not a valid PNG file.");
        }
    }
}
