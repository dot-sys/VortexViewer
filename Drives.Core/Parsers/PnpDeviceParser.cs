using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Drives.Core.Models;

// Queries PnP system via WMI and registry
namespace Drives.Core.Parsers
{
    // Extracts USB device info from WMI
    public static class PnpDeviceParser
    {
        public static List<UsbDeviceEntry> ParsePnpDevices()
        {
            var entries = new List<UsbDeviceEntry>();

            try
            {
                var registryEntries = UsbRegistryParser.ParseUsbStorRegistry();
                entries.AddRange(registryEntries);

                var connectedEntries = ParseConnectedDevices();
                entries.AddRange(connectedEntries);
            }
            catch
            {
            }

            return entries;
        }

        public static List<DiskDriveInfo> GetDiskDrives()
        {
            var drives = new List<DiskDriveInfo>();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject drive in searcher.Get())
                    {
                        try
                        {
                            var driveInfo = new DiskDriveInfo
                            {
                                DeviceID = drive["DeviceID"]?.ToString() ?? "",
                                Model = drive["Model"]?.ToString() ?? "",
                                SerialNumber = drive["SerialNumber"]?.ToString()?.Trim() ?? "",
                                PNPDeviceID = drive["PNPDeviceID"]?.ToString() ?? "",
                                InterfaceType = drive["InterfaceType"]?.ToString() ?? "",
                                MediaType = drive["MediaType"]?.ToString() ?? "",
                                Size = drive["Size"] != null ? Convert.ToInt64(drive["Size"]) : 0,
                                Partitions = drive["Partitions"] != null ? Convert.ToInt32(drive["Partitions"]) : 0,
                                Status = drive["Status"]?.ToString() ?? ""
                            };

                            if (drive["InstallDate"] != null)
                            {
                                try
                                {
                                    string installDateStr = drive["InstallDate"].ToString();
                                    driveInfo.InstallDate = ManagementDateTimeConverter.ToDateTime(installDateStr);
                                }
                                catch
                                {
                                    driveInfo.InstallDate = null;
                                }
                            }

                            drives.Add(driveInfo);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return drives;
        }

        public static List<VolumeDetailInfo> GetVolumes()
        {
            var volumes = new List<VolumeDetailInfo>();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Volume"))
                {
                    foreach (ManagementObject volume in searcher.Get())
                    {
                        try
                        {
                            var volumeInfo = new VolumeDetailInfo
                            {
                                DeviceID = volume["DeviceID"]?.ToString() ?? "",
                                DriveLetter = volume["DriveLetter"]?.ToString() ?? "",
                                Label = volume["Label"]?.ToString() ?? "",
                                FileSystem = volume["FileSystem"]?.ToString() ?? "",
                                Capacity = volume["Capacity"] != null ? Convert.ToInt64(volume["Capacity"]) : 0,
                                FreeSpace = volume["FreeSpace"] != null ? Convert.ToInt64(volume["FreeSpace"]) : 0,
                                DriveType = volume["DriveType"] != null ? Convert.ToUInt32(volume["DriveType"]) : 0,
                                SerialNumber = volume["SerialNumber"] != null ? Convert.ToUInt32(volume["SerialNumber"]) : 0
                            };

                            if (volume["InstallDate"] != null)
                            {
                                try
                                {
                                    string installDateStr = volume["InstallDate"].ToString();
                                    volumeInfo.InstallDate = ManagementDateTimeConverter.ToDateTime(installDateStr);
                                }
                                catch
                                {
                                    volumeInfo.InstallDate = null;
                                }
                            }

                            volumes.Add(volumeInfo);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return volumes;
        }

        public static List<PnpDeviceInfo> GetPnpDevicesByClass(params string[] classNames)
        {
            var devices = new List<PnpDeviceInfo>();

            if (classNames == null || classNames.Length == 0)
                return devices;

            try
            {
                var classFilter = string.Join("','", classNames);
                var query = $"SELECT * FROM Win32_PnPEntity WHERE ClassGuid IN ('{classFilter}')";

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        try
                        {
                            var className = device["PNPClass"]?.ToString() ?? "";
                            
                            bool matchesClass = false;
                            foreach (var targetClass in classNames)
                            {
                                if (className.Equals(targetClass, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchesClass = true;
                                    break;
                                }
                            }

                            if (!matchesClass)
                                continue;

                            var deviceInfo = new PnpDeviceInfo
                            {
                                DeviceID = device["DeviceID"]?.ToString() ?? "",
                                Name = device["Name"]?.ToString() ?? "",
                                PNPClass = className,
                                Manufacturer = device["Manufacturer"]?.ToString() ?? "",
                                Status = device["Status"]?.ToString() ?? "",
                                Present = device["Present"] != null && Convert.ToBoolean(device["Present"]),
                                Service = device["Service"]?.ToString() ?? ""
                            };

                            deviceInfo.InstallDate = UsbRegistryParser.GetDeviceInstallDate(deviceInfo.DeviceID);

                            devices.Add(deviceInfo);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return devices;
        }

        private static List<UsbDeviceEntry> ParseConnectedDevices()
        {
            var entries = new List<UsbDeviceEntry>();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE 'USBSTOR%'"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        try
                        {
                            var deviceId = device["DeviceID"]?.ToString();
                            var friendlyName = device["Name"]?.ToString();
                            var status = device["Status"]?.ToString();

                            if (string.IsNullOrEmpty(deviceId))
                                continue;

                            var parts = deviceId.Split('\\');
                            string serial = parts.Length > 2 ? parts[2].Replace("&0", "").Replace("&1", "") : "";

                            string deviceName = friendlyName?.Replace("USB Device", "").Trim() ?? "";

                            string action = "Connected";
                            if (status == "OK")
                            {
                                action = "Currently Connected";
                            }
                            else if (status == "Degraded")
                            {
                                action = "Connected (Degraded)";
                            }

                            entries.Add(new UsbDeviceEntry
                            {
                                Timestamp = DateTime.Now,
                                DeviceName = deviceName,
                                Serial = serial,
                                Action = action,
                                Log = "PnP"
                            });
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return entries;
        }

        public static List<RemovableDriveInfo> GetConnectedRemovableDrives()
        {
            var drives = new List<RemovableDriveInfo>();

            try
            {
                var driveInfos = DriveInfo.GetDrives();
                foreach (var drive in driveInfos)
                {
                    if (drive.DriveType == DriveType.Removable && drive.IsReady)
                    {
                        string driveLetter = drive.Name.TrimEnd('\\');
                        string volumeGuid = GetVolumeGuidForDrive(driveLetter);
                        drives.Add(new RemovableDriveInfo(driveLetter, volumeGuid));
                    }
                }
            }
            catch
            {
            }

            return drives;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumeNameForVolumeMountPoint(
            string lpszVolumeMountPoint,
            StringBuilder lpszVolumeName,
            uint cchBufferLength);

        private static string GetVolumeGuidForDrive(string driveLetter)
        {
            try
            {
                if (!driveLetter.EndsWith("\\"))
                    driveLetter += "\\";

                var volumeName = new StringBuilder(260);
                if (GetVolumeNameForVolumeMountPoint(driveLetter, volumeName, (uint)volumeName.Capacity))
                {
                    string volumeGuid = volumeName.ToString().Trim('\\');
                    
                    if (volumeGuid.Contains("{") && volumeGuid.Contains("}"))
                    {
                        int start = volumeGuid.IndexOf('{');
                        int end = volumeGuid.IndexOf('}') + 1;
                        return volumeGuid.Substring(start, end - start).ToLower();
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }

    // Detailed WMI disk drive properties
    public class DiskDriveInfo
    {
        public string DeviceID { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string PNPDeviceID { get; set; }
        public string InterfaceType { get; set; }
        public string MediaType { get; set; }
        public long Size { get; set; }
        public int Partitions { get; set; }
        public string Status { get; set; }
        public DateTime? InstallDate { get; set; }

        public DiskDriveInfo()
        {
            DeviceID = string.Empty;
            Model = string.Empty;
            SerialNumber = string.Empty;
            PNPDeviceID = string.Empty;
            InterfaceType = string.Empty;
            MediaType = string.Empty;
            Status = string.Empty;
        }

        public override string ToString()
        {
            return $"{DeviceID}\t{Model}\t{SerialNumber}\t{PNPDeviceID}\t{InstallDate:yyyy-MM-dd}";
        }
    }

    // Detailed WMI volume properties
    public class VolumeDetailInfo
    {
        public string DeviceID { get; set; }
        public string DriveLetter { get; set; }
        public string Label { get; set; }
        public string FileSystem { get; set; }
        public long Capacity { get; set; }
        public long FreeSpace { get; set; }
        public uint DriveType { get; set; }
        public uint SerialNumber { get; set; }
        public DateTime? InstallDate { get; set; }

        public VolumeDetailInfo()
        {
            DeviceID = string.Empty;
            DriveLetter = string.Empty;
            Label = string.Empty;
            FileSystem = string.Empty;
        }

        public override string ToString()
        {
            return $"{DriveLetter}\t{Label}\t{DeviceID}\t{InstallDate:yyyy-MM-dd}";
        }
    }

    // PnP device from WMI queries
    public class PnpDeviceInfo
    {
        public string DeviceID { get; set; }
        public string Name { get; set; }
        public string PNPClass { get; set; }
        public string Manufacturer { get; set; }
        public string Status { get; set; }
        public bool Present { get; set; }
        public string Service { get; set; }
        public DateTime? InstallDate { get; set; }

        public PnpDeviceInfo()
        {
            DeviceID = string.Empty;
            Name = string.Empty;
            PNPClass = string.Empty;
            Manufacturer = string.Empty;
            Status = string.Empty;
            Service = string.Empty;
        }

        public override string ToString()
        {
            return $"{Name}\t{PNPClass}\t{Status}\t{Present}\t{InstallDate:yyyy-MM-dd}";
        }
    }
}
