using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Volume serial number resolution utilities
namespace Timeline.Core.Util
{
    // Maps volume serials to drive letters
    public static class VolumeSerialNumberMapper
    {
        private static readonly Dictionary<uint, string> SerialNumberMappings;
        private static readonly Dictionary<string, string> HexSerialMappings;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer,
            int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer,
            int nFileSystemNameSize);

        static VolumeSerialNumberMapper()
        {
            SerialNumberMappings = new Dictionary<uint, string>();
            HexSerialMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            BuildMappings();
        }

        private static void BuildMappings()
        {
            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (!drive.IsReady) continue;

                    try
                    {
                        var volumeNameBuffer = new StringBuilder(256);
                        var fileSystemNameBuffer = new StringBuilder(256);
                        
                        if (GetVolumeInformation(
                            drive.RootDirectory.FullName,
                            volumeNameBuffer,
                            volumeNameBuffer.Capacity,
                            out uint serialNumber,
                            out uint maxComponentLength,
                            out uint fileSystemFlags,
                            fileSystemNameBuffer,
                            fileSystemNameBuffer.Capacity))
                        {
                            SerialNumberMappings[serialNumber] = drive.Name.TrimEnd('\\');
                            
                            string hexSerial = $"{(serialNumber >> 16):X4}-{(serialNumber & 0xFFFF):X4}";
                            HexSerialMappings[hexSerial] = drive.Name.TrimEnd('\\');
                            
                            string hexSerialNoDash = $"{serialNumber:X8}";
                            HexSerialMappings[hexSerialNoDash] = drive.Name.TrimEnd('\\');
                            
                            HexSerialMappings[hexSerialNoDash.ToLowerInvariant()] = drive.Name.TrimEnd('\\');
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public static string GetDriveLetterFromSerial(uint serialNumber)
        {
            if (SerialNumberMappings.TryGetValue(serialNumber, out var driveLetter))
                return driveLetter;
            
            return null;
        }

        public static string GetDriveLetterFromVolumeSerial(string volumeIdentifier)
        {
            if (string.IsNullOrEmpty(volumeIdentifier))
                return null;

            volumeIdentifier = volumeIdentifier.Replace("{", "").Replace("}", "").Replace("-", "");
            
            if (HexSerialMappings.TryGetValue(volumeIdentifier, out var driveLetter))
                return driveLetter;
            
            if (volumeIdentifier.Length >= 8)
            {
                var last8 = volumeIdentifier.Substring(volumeIdentifier.Length - 8);
                if (HexSerialMappings.TryGetValue(last8, out driveLetter))
                    return driveLetter;
            }
            
            if (volumeIdentifier.Length >= 8)
            {
                var first8 = volumeIdentifier.Substring(0, 8);
                if (HexSerialMappings.TryGetValue(first8, out driveLetter))
                    return driveLetter;
            }
            
            if (volumeIdentifier.Length <= 8)
            {
                try
                {
                    uint serial = Convert.ToUInt32(volumeIdentifier, 16);
                    return GetDriveLetterFromSerial(serial);
                }
                catch
                {
                }
            }
            
            return null;
        }

        public static string GetDriveLetterFromPrefetchVolume(string prefetchVolume)
        {
            if (string.IsNullOrEmpty(prefetchVolume))
                return null;

            prefetchVolume = prefetchVolume.Replace("{", "").Replace("}", "").Replace("-", "");
            
            if (HexSerialMappings.TryGetValue(prefetchVolume, out var driveLetter))
                return driveLetter;
            
            if (prefetchVolume.Length >= 8)
            {
                var last8 = prefetchVolume.Substring(prefetchVolume.Length - 8);
                if (HexSerialMappings.TryGetValue(last8, out driveLetter))
                    return driveLetter;
            }
            
            if (prefetchVolume.Length >= 8)
            {
                var first8 = prefetchVolume.Substring(0, 8);
                if (HexSerialMappings.TryGetValue(first8, out driveLetter))
                    return driveLetter;
            }
            
            if (prefetchVolume.Length <= 8)
            {
                try
                {
                    uint serial = Convert.ToUInt32(prefetchVolume, 16);
                    return GetDriveLetterFromSerial(serial);
                }
                catch
                {
                }
            }
            
            if (prefetchVolume.Length > 16)
            {
                var middle8 = prefetchVolume.Substring(8, 8);
                if (HexSerialMappings.TryGetValue(middle8, out driveLetter))
                    return driveLetter;
            }
            
            return null;
        }

        public static void RefreshMappings()
        {
            SerialNumberMappings.Clear();
            HexSerialMappings.Clear();
            BuildMappings();
        }

        public static List<string> GetDiagnosticLog()
        {
            var log = new List<string>();
            log.Add("=== Volume Serial Number Mappings ===");
            
            foreach (var kvp in SerialNumberMappings)
            {
                log.Add($"{kvp.Key:X8} -> {kvp.Value}");
            }
            
            log.Add($"Total mappings: {SerialNumberMappings.Count}");
            return log;
        }
    }
}
