using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Windows.Security.Credentials.UI;

namespace SystemTools.Services;

public enum WindowsHelloSupportStatus
{
    Available,
    UnsupportedSystem,
    FaceNotEnrolled,
    HelloNotConfigured,
    DisabledByPolicy,
    DeviceBusy,
    Unavailable,
    Error
}

public readonly record struct WindowsHelloSupportResult(WindowsHelloSupportStatus Status, string Message)
{
    public bool IsAvailable => Status == WindowsHelloSupportStatus.Available;
}

public static class WindowsHelloService
{
    // Microsoft documents the HWND-based desktop interop entry point as Windows build 22000+.
    public const int MinimumWindowsBuild = 22000;
    private const uint WinBioIdentityTypeSid = 3;
    private const uint WinBioTypeFacialFeatures = 0x00000002;
    private const int SecurityMaxSidSize = 68;

    public static async Task<WindowsHelloSupportResult> CheckSupportAsync(bool requireFaceEnrollment = true)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild))
        {
            return new WindowsHelloSupportResult(
                WindowsHelloSupportStatus.UnsupportedSystem,
                "Windows Hello 验证器需要 Windows 11（内部版本 22000）或更高版本。");
        }

        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            var unavailable = availability switch
            {
                UserConsentVerifierAvailability.Available => (WindowsHelloSupportResult?)null,
                UserConsentVerifierAvailability.NotConfiguredForUser => new(
                    WindowsHelloSupportStatus.HelloNotConfigured,
                    "当前 Windows 用户尚未配置 Windows Hello。"),
                UserConsentVerifierAvailability.DisabledByPolicy => new(
                    WindowsHelloSupportStatus.DisabledByPolicy,
                    "Windows Hello 已被系统策略禁用。"),
                UserConsentVerifierAvailability.DeviceBusy => new(
                    WindowsHelloSupportStatus.DeviceBusy,
                    "Windows Hello 设备正忙，请稍后重试。"),
                UserConsentVerifierAvailability.DeviceNotPresent => new(
                    WindowsHelloSupportStatus.Unavailable,
                    "系统未检测到可用的 Windows Hello 设备。"),
                _ => new WindowsHelloSupportResult(
                    WindowsHelloSupportStatus.Unavailable,
                    "Windows Hello 当前不可用。")
            };

            if (unavailable is { } result)
            {
                return result;
            }

            if (requireFaceEnrollment && !HasCurrentUserFaceEnrollment())
            {
                return new WindowsHelloSupportResult(
                    WindowsHelloSupportStatus.FaceNotEnrolled,
                    "当前 Windows 用户尚未录入 Windows Hello 人脸。请先在系统设置中完成人脸录入。");
            }

            return new WindowsHelloSupportResult(
                WindowsHelloSupportStatus.Available,
                "Windows Hello 已配置，可以使用系统验证。");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows Hello support check failed: {ex}");
            return new WindowsHelloSupportResult(
                WindowsHelloSupportStatus.Error,
                $"检查 Windows Hello 时发生错误：{ex.Message}");
        }
    }

    public static async Task<UserConsentVerificationResult> RequestVerificationAsync(nint windowHandle, string message)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, MinimumWindowsBuild))
        {
            return UserConsentVerificationResult.DeviceNotPresent;
        }

        if (windowHandle == 0)
        {
            throw new InvalidOperationException("无法获取 ClassIsland 认证窗口句柄。");
        }

        return await UserConsentVerifierInterop.RequestVerificationForWindowAsync(windowHandle, message);
    }

    public static bool OpenWindowsHelloSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:signinoptions-launchfaceenrollment",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open Windows Hello settings: {ex}");
            return false;
        }
    }

    private static bool HasCurrentUserFaceEnrollment()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.GetBinaryFormBytes();
        if (sid == null || sid.Length == 0 || sid.Length > SecurityMaxSidSize)
        {
            return false;
        }

        var winBioIdentity = new WinBioIdentity { Type = WinBioIdentityTypeSid };
        unsafe
        {
            byte* value = winBioIdentity.Value;
            *(uint*)value = (uint)sid.Length;
            sid.CopyTo(new Span<byte>(value + sizeof(uint), SecurityMaxSidSize));
        }

        var hr = WinBioGetEnrolledFactors(ref winBioIdentity, out var factors);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return (factors & WinBioTypeFacialFeatures) != 0;
    }

    private static byte[] GetBinaryFormBytes(this SecurityIdentifier sid)
    {
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return bytes;
    }

    [DllImport("winbio.dll", ExactSpelling = true)]
    private static extern int WinBioGetEnrolledFactors(
        ref WinBioIdentity accountOwner,
        out uint enrolledFactors);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WinBioIdentity
    {
        public uint Type;
        public fixed byte Value[sizeof(uint) + SecurityMaxSidSize];
    }
}
