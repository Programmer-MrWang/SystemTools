using System;
using System.IO;
using System.Management;
using System.Threading.Tasks;

namespace SystemTools.Helpers;

/// <summary>
/// 判定卷是否位于 USB 存储设备上的工具。
/// </summary>
/// <remarks>
/// Windows 会把相当一部分 U 盘、移动固态盘识别为固定磁盘
/// （<see cref="DriveType.Fixed"/>，而非 <see cref="DriveType.Removable"/>），
/// 因此“是否 U 盘”不能只看 <see cref="DriveType"/>，
/// 需要回溯卷所在物理磁盘的总线类型（USB）来判定。
/// </remarks>
public static class UsbStorageUtils
{
    private const ushort BusTypeUsb = 7; // MSFT_Disk.BusType：7 = USB
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";
    private const string CimV2Namespace = @"root\cimv2";

    /// <summary>卷的存储类型判定结果。</summary>
    /// <param name="IsUsbStorage">卷所在物理磁盘是否经 USB 总线连接。</param>
    /// <param name="DiskNumber">卷所在物理磁盘编号，无法确定时为 <c>null</c>。</param>
    public readonly record struct UsbVolumeInfo(bool IsUsbStorage, uint? DiskNumber)
    {
        /// <summary>无法判定为 USB 存储卷时的默认结果。</summary>
        public static UsbVolumeInfo NotUsb => new(false, null);
    }

    /// <summary>
    /// 判断指定卷是否位于 USB 存储设备上。
    /// </summary>
    public static bool IsUsbStorageVolume(string driveRoot) => Classify(driveRoot).IsUsbStorage;

    /// <summary>
    /// 判断指定卷是否为可移动介质卷（传统的 U 盘/存储卡判据）。
    /// </summary>
    public static bool IsRemovableVolume(string driveRoot)
    {
        try
        {
            var info = new DriveInfo(driveRoot);
            return info.IsReady && info.DriveType == DriveType.Removable;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判定卷的存储类型，优先使用 Storage 命名空间的总线类型，
    /// 不可用时回退到 CIMv2 关联查询。
    /// </summary>
    public static UsbVolumeInfo Classify(string driveRoot)
    {
        if (!TryGetDriveLetter(driveRoot, out var letter))
        {
            return UsbVolumeInfo.NotUsb;
        }

        // 首选：Storage 命名空间可正确处理 UASP 等以 SCSI 形式呈现的 USB 设备。
        if (TryClassifyViaStorageNamespace(letter, out var diskNumber, out var isUsb))
        {
            return new UsbVolumeInfo(isUsb, diskNumber);
        }

        // 回退：CIMv2 关联查询，兼容不支持 Storage 命名空间的系统。
        if (TryClassifyViaCimV2(letter, out diskNumber, out isUsb))
        {
            return new UsbVolumeInfo(isUsb, diskNumber);
        }

        return UsbVolumeInfo.NotUsb;
    }

    /// <summary>
    /// 等待卷挂载就绪。卷插入事件到达时卷可能尚未挂载完成，需要重试等待。
    /// </summary>
    public static async Task<bool> WaitUntilReadyAsync(string driveRoot, int attempts = 10, int delayMilliseconds = 200)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (new DriveInfo(driveRoot).IsReady)
                {
                    return true;
                }
            }
            catch
            {
                // 卷尚未创建，等待后重试。
            }

            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
        }

        return false;
    }

    private static bool TryClassifyViaStorageNamespace(char letter, out uint? diskNumber, out bool isUsb)
    {
        diskNumber = null;
        isUsb = false;

        try
        {
            var scope = new ManagementScope(StorageNamespace);
            scope.Connect();

            uint? foundDiskNumber = null;
            using (var partitionSearcher = new ManagementObjectSearcher(scope,
                       new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition")))
            using (var partitions = partitionSearcher.Get())
            {
                foreach (ManagementBaseObject item in partitions)
                {
                    using var partition = (ManagementObject)item;
                    if (!TryConvertDriveLetter(partition["DriveLetter"], out var current) || current != letter)
                    {
                        continue;
                    }

                    if (partition["DiskNumber"] is null)
                    {
                        continue;
                    }

                    foundDiskNumber = Convert.ToUInt32(partition["DiskNumber"]);
                    break;
                }
            }

            if (foundDiskNumber is null)
            {
                return false;
            }

            using (var diskSearcher = new ManagementObjectSearcher(scope,
                       new ObjectQuery($"SELECT BusType FROM MSFT_Disk WHERE Number = {foundDiskNumber.Value}")))
            using (var disks = diskSearcher.Get())
            {
                foreach (ManagementBaseObject item in disks)
                {
                    using var disk = (ManagementObject)item;
                    if (disk["BusType"] is null)
                    {
                        continue;
                    }

                    diskNumber = foundDiskNumber;
                    isUsb = Convert.ToUInt16(disk["BusType"]) == BusTypeUsb;
                    return true;
                }
            }
        }
        catch
        {
            // Storage 命名空间不可用时静默回退。
        }

        diskNumber = null;
        isUsb = false;
        return false;
    }

    private static bool TryClassifyViaCimV2(char letter, out uint? diskNumber, out bool isUsb)
    {
        diskNumber = null;
        isUsb = false;

        try
        {
            var scope = new ManagementScope(CimV2Namespace);
            scope.Connect();

            using var logicalDisk = new ManagementObject(scope,
                new ManagementPath($"Win32_LogicalDisk.DeviceID='{letter}:'"), null);

            foreach (ManagementBaseObject partitionItem in logicalDisk.GetRelated("Win32_DiskPartition"))
            {
                using var partition = (ManagementObject)partitionItem;
                foreach (ManagementBaseObject diskItem in partition.GetRelated("Win32_DiskDrive"))
                {
                    using var disk = (ManagementObject)diskItem;

                    var interfaceType = disk["InterfaceType"]?.ToString();
                    var pnpDeviceId = disk["PNPDeviceID"]?.ToString();

                    diskNumber = disk["Index"] is null ? null : Convert.ToUInt32(disk["Index"]);
                    isUsb = string.Equals(interfaceType, "USB", StringComparison.OrdinalIgnoreCase)
                            || (pnpDeviceId?.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase) ?? false);
                    return true;
                }
            }
        }
        catch
        {
            // 关联查询失败时静默回退。
        }

        diskNumber = null;
        isUsb = false;
        return false;
    }

    private static bool TryGetDriveLetter(string driveRoot, out char letter)
    {
        letter = '\0';

        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            return false;
        }

        try
        {
            var root = Path.GetPathRoot(driveRoot);
            if (string.IsNullOrEmpty(root) || !char.IsLetter(root[0]))
            {
                return false;
            }

            letter = char.ToUpperInvariant(root[0]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConvertDriveLetter(object? value, out char letter)
    {
        letter = '\0';

        switch (value)
        {
            case char c when char.IsLetter(c):
                letter = char.ToUpperInvariant(c);
                return true;
            case ushort u when u != 0 && char.IsLetter((char)u):
                letter = char.ToUpperInvariant((char)u);
                return true;
            case string s when s.Length > 0 && char.IsLetter(s[0]):
                letter = char.ToUpperInvariant(s[0]);
                return true;
            default:
                return false;
        }
    }
}
