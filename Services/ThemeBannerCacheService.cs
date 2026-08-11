using ClassIsland.Core;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SystemTools.Services;

public sealed class ThemeBannerCacheService(ILogger<ThemeBannerCacheService> logger)
{
    private const string BannerDownloadUrl =
        "https://livefile.xesimg.com/programme/python_assets/a26b478872e7986800787a3b77d5a06e.png";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public static string BannerPath => Path.GetFullPath(
        Path.Combine(CommonDirectories.AppCacheFolderPath, "SystemTools", "banner.png"));

    public void Start() => _ = EnsureBannerAsync();

    private async Task EnsureBannerAsync()
    {
        await _downloadLock.WaitAsync();
        var temporaryPath = BannerPath + ".download";
        try
        {
            logger.LogInformation("正在检查主题预览图缓存：{BannerPath}", BannerPath);
            if (File.Exists(BannerPath))
            {
                logger.LogInformation("主题预览图缓存已存在，跳过后台下载。");
                return;
            }

            var cacheDirectory = Path.GetDirectoryName(BannerPath)!;
            Directory.CreateDirectory(cacheDirectory);
            logger.LogInformation("主题预览图缓存不存在，开始从 {DownloadUrl} 后台下载。", BannerDownloadUrl);

            using var response = await HttpClient.GetAsync(
                BannerDownloadUrl,
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
            File.Move(temporaryPath, BannerPath, overwrite: true);
            logger.LogInformation("主题预览图后台下载完成：{BannerPath}", BannerPath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "主题预览图后台下载失败，将在下次启动时重试。");
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to delete the temporary theme banner.");
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
            throw new InvalidDataException("The downloaded theme banner is not a valid PNG file.");
        }
    }
}
