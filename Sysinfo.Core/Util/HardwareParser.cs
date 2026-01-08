using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using SysInfo.Core.Models;

// Hardware information extraction utilities
namespace SysInfo.Core.Util
{
    // Extracts hardware details via WMI queries
    public static class HardwareParser
    {
        // Collects all hardware information into single object
        public static HardwareInfo GetHardwareInfo()
        {
            var info = new HardwareInfo();

            try
            {
                info.CpuModel = GetWmiProperty("Win32_Processor", "Name");
                info.CpuSerial = GetWmiProperty("Win32_Processor", "ProcessorId");
                
                GetGpuInfo(out string gpuChipset, out string gpuName);
                info.GpuChipset = gpuChipset;
                info.GpuModel = gpuName;
                
                info.MotherboardModel = GetWmiProperty("Win32_BaseBoard", "Product");
                info.MotherboardSerial = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
                
                info.BiosVendor = GetWmiProperty("Win32_BIOS", "Manufacturer");
                info.BiosVersion = GetWmiProperty("Win32_BIOS", "SMBIOSBIOSVersion");
                info.BiosUuid = GetWmiProperty("Win32_ComputerSystemProduct", "UUID");
                
                GetSystemDriveInfo(out string driveModel, out string driveSerial);
                info.HardDriveModel = driveModel;
                info.HardDriveSerial = driveSerial;
                
                info.NetworkMacAddresses = GetNetworkMacAddresses();
            }
            catch (Exception ex)
            {
                info.CpuModel = $"Error: {ex.Message}";
            }

            return info;
        }

        // Returns system drive letter from environment path
        private static string GetSystemDrive()
        {
            var systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var drive = systemFolder.Substring(0, 2);
            return string.IsNullOrEmpty(drive) ? "C:" : drive;
        }

        // Queries WMI class for specific property value
        private static string GetWmiProperty(string wmiClass, string propertyName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {wmiClass}"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection.Cast<ManagementObject>())
                    {
                        var value = obj[propertyName];
                        if (value != null)
                            return value.ToString().Trim();
                    }
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Retrieves all network adapter MAC addresses
        private static string GetNetworkMacAddresses()
        {
            try
            {
                var sb = new StringBuilder();
                using (var searcher = new ManagementObjectSearcher("SELECT MACAddress FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection.Cast<ManagementObject>())
                    {
                        var mac = obj["MACAddress"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(mac))
                        {
                            if (sb.Length > 0)
                                sb.Append(", ");
                            sb.Append(mac);
                        }
                    }
                }

                return sb.Length > 0 ? sb.ToString() : "Unavailable";
            }
            catch
            {
                return "Unavailable";
            }
        }

        // Extracts GPU chipset and SUBSYS ID from PNP Device ID
        private static void GetGpuInfo(out string chipset, out string name)
        {
            chipset = "Unavailable";
            name = "Unavailable";

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection.Cast<ManagementObject>())
                    {
                        var controllerName = obj["Name"]?.ToString();
                        var pnpDeviceId = obj["PNPDeviceID"]?.ToString();
                        
                        if (!string.IsNullOrWhiteSpace(controllerName))
                            chipset = controllerName.Trim();
                        
                        name = ExtractSubsysId(pnpDeviceId) ?? "No SUBSYS";
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        // Parses subsystem ID from PNP device string
        private static string ExtractSubsysId(string pnpDeviceId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId))
                return null;

            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    pnpDeviceId, 
                    @"VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}&SUBSYS_([0-9A-F]{8})", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match.Success)
                    return match.Groups[1].Value.ToUpperInvariant();
            }
            catch
            {
            }

            return null;
        }

        // Retrieves system drive model and serial number
        private static void GetSystemDriveInfo(out string model, out string serial)
        {
            model = "Unavailable";
            serial = "Unavailable";

            try
            {
                var systemDrive = GetSystemDrive();

                using (var logicalSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                using (var partitions = logicalSearcher.Get())
                {
                    foreach (ManagementObject partition in partitions.Cast<ManagementObject>())
                    {
                        var partitionDeviceId = partition["DeviceID"]?.ToString();
                        if (string.IsNullOrEmpty(partitionDeviceId))
                            continue;

                        using (var diskSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                        using (var disks = diskSearcher.Get())
                        {
                            foreach (ManagementObject diskDrive in disks.Cast<ManagementObject>())
                            {
                                model = diskDrive["Model"]?.ToString()?.Trim() ?? "Unavailable";
                                serial = diskDrive["SerialNumber"]?.ToString()?.Trim() ?? "Unavailable";
                                return;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
