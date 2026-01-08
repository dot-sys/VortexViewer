using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Drives.Core.Models;

// Queries system volumes via WMI
namespace Drives.Core.Parsers
{
    // Gathers volume info from WMI
    public static class VolumeInfoParser
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumeNameForVolumeMountPoint(
            string lpszVolumeMountPoint,
            StringBuilder lpszVolumeName,
            uint cchBufferLength);

        public static List<VolumeInfo> GetVolumeInformation()
        {
            var volumes = new List<VolumeInfo>();

            try
            {
                var driveInfos = DriveInfo.GetDrives();
                var diskInfoMap = GetDiskInformation();

                foreach (var drive in driveInfos)
                {
                    if (!drive.IsReady)
                        continue;

                    try
                    {
                        var volumeInfo = new VolumeInfo
                        {
                            Drive = drive.Name.TrimEnd('\\').Substring(0, 1),
                            FileSystem = drive.DriveFormat,
                            SizeBytes = drive.TotalSize,
                            Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel.Trim()
                        };

                        // Format size
                        if (volumeInfo.SizeBytes >= 1073741824) // 1 GB
                        {
                            volumeInfo.Size = $"{volumeInfo.SizeBytes / 1073741824.0:N2} GB";
                        }
                        else
                        {
                            volumeInfo.Size = $"{Math.Round(volumeInfo.SizeBytes / 1048576.0, 0):N0} MB";
                        }

                        // Get partition ID (Volume GUID)
                        volumeInfo.PartitionID = GetVolumeGuid(drive.Name);

                        // Get disk number, bus type, and serial from WMI
                        var diskInfo = GetDiskInfoForDrive(drive.Name);
                        if (diskInfo != null)
                        {
                            volumeInfo.DiskNumber = diskInfo.DiskNumber;
                            volumeInfo.BusType = diskInfo.BusType;
                            volumeInfo.Serial = diskInfo.Serial;
                        }

                        // Get format date from System Volume Information folder
                        volumeInfo.FormatDate = GetFormatDate(drive.Name);

                        volumes.Add(volumeInfo);
                    }
                    catch
                    {
                        // Skip drives that can't be queried
                    }
                }

                // Sort by disk number ascending, then by size descending
                volumes.Sort((a, b) =>
                {
                    int diskCompare = a.DiskNumber.CompareTo(b.DiskNumber);
                    if (diskCompare != 0)
                        return diskCompare;
                    return b.SizeBytes.CompareTo(a.SizeBytes);
                });
            }
            catch
            {
                // Failed to enumerate drives
            }

            return volumes;
        }

        private static string GetVolumeGuid(string driveLetter)
        {
            try
            {
                if (!driveLetter.EndsWith("\\"))
                    driveLetter += "\\";

                var volumeName = new StringBuilder(260);
                if (GetVolumeNameForVolumeMountPoint(driveLetter, volumeName, (uint)volumeName.Capacity))
                {
                    string volumeGuid = volumeName.ToString().Trim('\\');
                    
                    // Extract GUID from format like "\\?\Volume{GUID}\"
                    if (volumeGuid.Contains("{") && volumeGuid.Contains("}"))
                    {
                        int start = volumeGuid.IndexOf('{');
                        int end = volumeGuid.IndexOf('}') + 1;
                        return volumeGuid.Substring(start, end - start);
                    }
                }
            }
            catch
            {
                // Failed to get volume GUID
            }

            return string.Empty;
        }

        // Checks creation time to estimate format date
        private static DateTime? GetFormatDate(string driveLetter)
        {
            try
            {
                string sysVolInfoPath = Path.Combine(driveLetter, "System Volume Information");
                if (Directory.Exists(sysVolInfoPath))
                {
                    var dirInfo = new DirectoryInfo(sysVolInfoPath);
                    return dirInfo.CreationTime;
                }
            }
            catch
            {
                // Can't access System Volume Information
            }

            return null;
        }

        private static Dictionary<string, DiskInfo> GetDiskInformation()
        {
            var diskInfo = new Dictionary<string, DiskInfo>();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                using (var diskCollection = searcher.Get())
                {
                    foreach (ManagementObject disk in diskCollection)
                    {
                        try
                        {
                            string deviceId = disk["DeviceID"]?.ToString();
                            int diskNumber = Convert.ToInt32(deviceId?.Replace("\\\\.\\PHYSICALDRIVE", "") ?? "0");
                            string interfaceType = disk["InterfaceType"]?.ToString() ?? "Unknown";
                            string serialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "";

                            // Get partitions for this disk
                            using (var partSearcher = new ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                            using (var partitionCollection = partSearcher.Get())
                            {
                                foreach (ManagementObject partition in partitionCollection)
                                {
                                    try
                                    {
                                        string partDeviceId = partition["DeviceID"]?.ToString();

                                        // Get logical disks for this partition
                                        using (var logicalSearcher = new ManagementObjectSearcher(
                                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partDeviceId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                                        using (var logicalCollection = logicalSearcher.Get())
                                        {
                                            foreach (ManagementObject logical in logicalCollection)
                                            {
                                                try
                                                {
                                                    string driveLetter = logical["DeviceID"]?.ToString();
                                                    if (!string.IsNullOrEmpty(driveLetter))
                                                    {
                                                        diskInfo[driveLetter] = new DiskInfo(diskNumber, interfaceType, serialNumber);
                                                    }
                                                }
                                                finally
                                                {
                                                    logical?.Dispose();
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        partition?.Dispose();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Skip problematic disks
                        }
                        finally
                        {
                            disk?.Dispose();
                        }
                    }
                }
            }
            catch
            {
                // WMI query failed
            }

            return diskInfo;
        }

        private static DiskInfo GetDiskInfoForDrive(string driveLetter)
        {
            try
            {
                driveLetter = driveLetter.TrimEnd('\\');

                using (var searcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                using (var partitionCollection = searcher.Get())
                {
                    foreach (ManagementObject partition in partitionCollection)
                    {
                        try
                        {
                            string partDeviceId = partition["DeviceID"]?.ToString();

                            using (var diskSearcher = new ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partDeviceId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                            using (var diskCollection = diskSearcher.Get())
                            {
                                foreach (ManagementObject disk in diskCollection)
                                {
                                    try
                                    {
                                        string deviceId = disk["DeviceID"]?.ToString();
                                        int diskNumber = Convert.ToInt32(deviceId?.Replace("\\\\.\\PHYSICALDRIVE", "") ?? "0");
                                        string interfaceType = disk["InterfaceType"]?.ToString() ?? "Unknown";
                                        string serialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "";

                                        return new DiskInfo(diskNumber, interfaceType, serialNumber);
                                    }
                                    finally
                                    {
                                        disk?.Dispose();
                                    }
                                }
                            }
                        }
                        finally
                        {
                            partition?.Dispose();
                        }
                    }
                }
            }
            catch
            {
                // Failed to get disk info
            }

            return null;
        }
    }
}
