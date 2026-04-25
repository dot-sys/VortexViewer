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
                // Use 64-bit registry view for CPU info
                using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    info.CpuModel = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unavailable";
                    // CPU Serial (ProcessorId) is best retrieved via WMI
                    info.CpuSerial = GetWmiProperty("Win32_Processor", "ProcessorId");
                }
                
                GetGpuInfo(out string gpuChipset, out string gpuName);
                info.GpuChipset = gpuChipset;
                info.GpuModel = gpuName;
                
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    info.MotherboardModel = key?.GetValue("BaseBoardProduct")?.ToString()?.Trim() ?? "Unavailable";
                    
                    // WMI is required for 100% correct Hardware Serials
                    info.MotherboardSerial = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
                    if (string.IsNullOrEmpty(info.MotherboardSerial) || info.MotherboardSerial == "Unavailable")
                    {
                        info.MotherboardSerial = key?.GetValue("BaseBoardSerialNumber")?.ToString()?.Trim() ?? "Unavailable";
                    }
                    
                    info.BiosVendor = key?.GetValue("BIOSVendor")?.ToString()?.Trim() ?? "Unavailable";
                    
                    var biosVersion = key?.GetValue("BIOSVersion");
                    if (biosVersion is string[] versionArray)
                        info.BiosVersion = string.Join(" ", versionArray).Trim();
                    else
                        info.BiosVersion = biosVersion?.ToString()?.Trim() ?? "Unavailable";
                        
                    // BIOS UUID MUST come from WMI for correctness
                    info.BiosUuid = GetWmiProperty("Win32_ComputerSystemProduct", "UUID");
                }
                
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
                var macs = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && 
                                  nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .Select(nic => nic.GetPhysicalAddress().ToString())
                    .Where(mac => !string.IsNullOrEmpty(mac))
                    .Select(mac => string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2))));

                var result = string.Join(", ", macs);
                return string.IsNullOrEmpty(result) ? "Unavailable" : result;
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
