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
                    foreach (ManagementObject obj in collection)
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
                    foreach (ManagementObject obj in collection)
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

        // Extracts GPU chipset and OEM model name
        private static void GetGpuInfo(out string chipset, out string name)
        {
            chipset = "Unavailable";
            name = "Unavailable";

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, InfFilename, InfSection FROM Win32_VideoController"))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        var controllerName = obj["Name"]?.ToString();
                        var pnpDeviceId = obj["PNPDeviceID"]?.ToString();
                        var infFilename = obj["InfFilename"]?.ToString();
                        var infSection = obj["InfSection"]?.ToString();
                        
                        if (!string.IsNullOrWhiteSpace(controllerName))
                            chipset = controllerName.Trim();
                        
                        name = GetGpuOemModel(pnpDeviceId, infFilename, infSection) ?? chipset;
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        // Resolves GPU OEM model from hardware identifiers
        private static string GetGpuOemModel(string pnpDeviceId, string infFilename, string infSection)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId))
                return null;

            var subsysId = ExtractSubsysId(pnpDeviceId);
            if (!string.IsNullOrWhiteSpace(subsysId))
            {
                var database = GetGpuSubsysDatabase();
                if (database.TryGetValue(subsysId, out var modelName))
                    return modelName;
            }

            if (!string.IsNullOrWhiteSpace(infFilename) && !string.IsNullOrWhiteSpace(infSection))
            {
                var infModel = ParseInfFileForModel(infFilename, infSection);
                if (!string.IsNullOrWhiteSpace(infModel))
                    return infModel;
            }

            return null;
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
                    @"SUBSYS_([0-9A-F]{8})", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match.Success)
                    return match.Groups[1].Value.ToUpperInvariant();
            }
            catch
            {
            }

            return null;
        }

        // Returns GPU subsystem ID to model mapping
        private static Dictionary<string, string> GetGpuSubsysDatabase()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "67051EAE", "XFX RX 6800 Speedster MERC/QICK319" },
                { "31661043", "ASUS TUF Gaming Radeon RX 6800 OC" },
                { "E4111462", "MSI Radeon RX 6800 Gaming X Trio" },
                { "14621468", "MSI Radeon RX 6800 XT Gaming X Trio" },
                { "67041EAE", "XFX RX 6800 XT Speedster MERC 319" },
                { "31711043", "ASUS ROG Strix Radeon RX 6800 XT OC" },
                { "E4081462", "MSI Radeon RX 6900 XT Gaming X Trio" },
                { "67031EAE", "XFX RX 6900 XT Speedster MERC 319" },
                { "86211043", "ASUS TUF Gaming GeForce RTX 3080" },
                { "39721462", "MSI GeForce RTX 3080 Gaming X Trio" },
                { "14581462", "MSI GeForce RTX 3080 Ti Suprim X" },
                { "14821462", "MSI GeForce RTX 3090 Gaming X Trio" },
                { "39831462", "MSI GeForce RTX 3090 Ti Suprim X" },
                { "88871043", "ASUS ROG Strix GeForce RTX 3090" },
                { "14081462", "MSI GeForce RTX 4080 Gaming X Trio" },
                { "14241462", "MSI GeForce RTX 4090 Gaming X Trio" },
                { "889A1043", "ASUS TUF Gaming GeForce RTX 4090 OC" },
                { "32061043", "ASUS Radeon RX 7900 XTX TUF Gaming OC" },
                { "51511002", "AMD Radeon RX 7900 XTX Reference" },
                { "51521002", "AMD Radeon RX 7900 XT Reference" },
                { "E4471462", "MSI Radeon RX 7900 XTX Gaming Trio" },
                { "RTX3050", "NVIDIA GeForce RTX 3050 8GB" },
                { "RTX3060", "NVIDIA GeForce RTX 3060 12GB" },
                { "RTX3060Ti", "NVIDIA GeForce RTX 3060 Ti" },
                { "RTX3070", "NVIDIA GeForce RTX 3070" },
                { "RTX3070Ti", "NVIDIA GeForce RTX 3070 Ti" },
                { "RTX3080-10", "NVIDIA GeForce RTX 3080 10GB" },
                { "RTX3080-12", "NVIDIA GeForce RTX 3080 12GB" },
                { "RTX3080Ti", "NVIDIA GeForce RTX 3080 Ti" },
                { "RTX3090", "NVIDIA GeForce RTX 3090" },
                { "RTX3090Ti", "NVIDIA GeForce RTX 3090 Ti" },
                { "RTX4060", "NVIDIA GeForce RTX 4060" },
                { "RTX4060Ti-8", "NVIDIA GeForce RTX 4060 Ti 8GB" },
                { "RTX4060Ti-16", "NVIDIA GeForce RTX 4060 Ti 16GB" },
                { "RTX4070", "NVIDIA GeForce RTX 4070" },
                { "RTX4070Super", "NVIDIA GeForce RTX 4070 Super" },
                { "RTX4070Ti", "NVIDIA GeForce RTX 4070 Ti" },
                { "RTX4070TiSuper", "NVIDIA GeForce RTX 4070 Ti Super" },
                { "RTX4080", "NVIDIA GeForce RTX 4080" },
                { "RTX4080Super", "NVIDIA GeForce RTX 4080 Super" },
                { "RTX4090", "NVIDIA GeForce RTX 4090" },
                { "RX6600", "AMD Radeon RX 6600" },
                { "RX6600XT", "AMD Radeon RX 6600 XT" },
                { "RX6650XT", "AMD Radeon RX 6650 XT" },
                { "RX6700", "AMD Radeon RX 6700" },
                { "RX6700XT", "AMD Radeon RX 6700 XT" },
                { "RX6750XT", "AMD Radeon RX 6750 XT" },
                { "RX6800", "AMD Radeon RX 6800" },
                { "RX6800XT", "AMD Radeon RX 6800 XT" },
                { "RX6900XT", "AMD Radeon RX 6900 XT" },
                { "RX6950XT", "AMD Radeon RX 6950 XT" },
                { "RX7600", "AMD Radeon RX 7600" },
                { "RX7600XT", "AMD Radeon RX 7600 XT" },
                { "RX7700XT", "AMD Radeon RX 7700 XT" },
                { "RX7800XT", "AMD Radeon RX 7800 XT" },
                { "RX7900XT", "AMD Radeon RX 7900 XT" },
                { "RX7900XTX", "AMD Radeon RX 7900 XTX" }
            };
        }

        // Extracts GPU model name from driver INF file
        private static string ParseInfFileForModel(string infFilename, string infSection)
        {
            if (string.IsNullOrWhiteSpace(infFilename) || string.IsNullOrWhiteSpace(infSection))
                return null;

            try
            {
                var infPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "INF",
                    infFilename);

                if (!System.IO.File.Exists(infPath))
                    return null;

                var lines = System.IO.File.ReadAllLines(infPath);
                bool inTargetSection = false;
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                    {
                        var sectionName = trimmedLine.Substring(1, trimmedLine.Length - 2);
                        inTargetSection = sectionName.Equals(infSection, StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (inTargetSection && !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith(";"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(
                            trimmedLine,
                            @"""([^""]+)""",
                            System.Text.RegularExpressions.RegexOptions.None);
                        
                        if (match.Success)
                        {
                            var deviceDesc = match.Groups[1].Value;
                            if (!deviceDesc.Contains("AMD") && 
                                !deviceDesc.Contains("NVIDIA") && 
                                !deviceDesc.Contains("Intel") &&
                                deviceDesc.Length > 10)
                            {
                                return deviceDesc;
                            }
                        }
                    }
                }
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
                    foreach (ManagementObject partition in partitions)
                    {
                        var partitionDeviceId = partition["DeviceID"]?.ToString();
                        if (string.IsNullOrEmpty(partitionDeviceId))
                            continue;

                        using (var diskSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                        using (var disks = diskSearcher.Get())
                        {
                            foreach (ManagementObject diskDrive in disks)
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
