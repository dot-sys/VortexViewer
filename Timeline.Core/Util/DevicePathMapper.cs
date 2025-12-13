using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Device path resolution utilities
namespace Timeline.Core.Util
{
    // Resolves device paths to drive letters
    public static class DevicePathMapper
    {
        private static readonly Dictionary<string, string> VolumeMappings;
        private static readonly Dictionary<string, string> VolumeGuidMappings;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumePathNameW(string lpszFileName, [Out] StringBuilder lpszVolumePathName, uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindFirstVolume(StringBuilder lpszVolumeName, uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextVolume(IntPtr hFindVolume, StringBuilder lpszVolumeName, uint cchBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindVolumeClose(IntPtr hFindVolume);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetVolumePathNamesForVolumeName(
            string lpszVolumeName,
            StringBuilder lpszVolumePathNames,
            uint cchBufferLength,
            ref uint lpcchReturnLength);

        static DevicePathMapper()
        {
            VolumeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            VolumeGuidMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            BuildDevicePathMappings();
            BuildVolumeGuidMappings();
        }

        private static void BuildDevicePathMappings()
        {
            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (!drive.IsReady) continue;

                    var dosDeviceName = drive.Name.Substring(0, 2);
                    var targetPath = new StringBuilder(260);
                    
                    if (QueryDosDevice(dosDeviceName, targetPath, targetPath.Capacity) != 0)
                    {
                        VolumeMappings[targetPath.ToString()] = drive.Name.TrimEnd('\\');
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static void BuildVolumeGuidMappings()
        {
            try
            {
                var volumeName = new StringBuilder(260);
                var hFindVolume = FindFirstVolume(volumeName, (uint)volumeName.Capacity);

                if (hFindVolume == IntPtr.Zero || hFindVolume.ToInt64() == -1)
                    return;

                try
                {
                    do
                    {
                        var volumePathNames = new StringBuilder(1024);
                        uint returnLength = 0;

                        if (GetVolumePathNamesForVolumeName(
                            volumeName.ToString(),
                            volumePathNames,
                            (uint)volumePathNames.Capacity,
                            ref returnLength))
                        {
                            var paths = ParseMultiString(volumePathNames.ToString());
                            
                            if (paths.Count > 0)
                            {
                                var driveLetter = paths[0].TrimEnd('\\');
                                var volumeGuid = volumeName.ToString().TrimEnd('\\');
                                
                                VolumeGuidMappings[volumeGuid] = driveLetter;
                                
                                if (volumeGuid.StartsWith(@"\\?\\"))
                                {
                                    VolumeGuidMappings[volumeGuid.Substring(4)] = driveLetter;
                                }
                            }
                        }

                        volumeName.Clear();
                    }
                    while (FindNextVolume(hFindVolume, volumeName, (uint)volumeName.Capacity));
                }
                finally
                {
                    FindVolumeClose(hFindVolume);
                }
            }
            catch (Exception)
            {
            }
        }

        public static List<string> GetDiagnosticLog()
        {
            return new List<string>();
        }

        private static List<string> ParseMultiString(string multiString)
        {
            var result = new List<string>();
            
            if (string.IsNullOrEmpty(multiString))
                return result;

            var parts = multiString.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            result.AddRange(parts);
            
            return result;
        }

        public static string TranslateDevicePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            foreach (var mapping in VolumeMappings)
            {
                if (path.StartsWith(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Replace(mapping.Key, mapping.Value);
                }
            }

            foreach (var mapping in VolumeGuidMappings)
            {
                if (path.StartsWith(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Replace(mapping.Key, mapping.Value);
                }
            }

            return path;
        }

        public static string GetDriveLetterFromVolume(string volumeGuid)
        {
            if (string.IsNullOrEmpty(volumeGuid))
                return null;

            if (VolumeGuidMappings.TryGetValue(volumeGuid, out var driveLetter))
                return driveLetter;

            if (volumeGuid.StartsWith(@"\\?\\"))
            {
                var trimmed = volumeGuid.Substring(4);
                if (VolumeGuidMappings.TryGetValue(trimmed, out driveLetter))
                    return driveLetter;
            }

            return null;
        }

        public static string GetDriveLetterFromPartialGuid(string partialGuid)
        {
            if (string.IsNullOrEmpty(partialGuid))
                return null;

            var normalizedPartial = partialGuid.Replace("-", "").Replace("{", "").Replace("}", "").ToLowerInvariant();

            foreach (var kvp in VolumeGuidMappings)
            {
                var fullVolumePath = kvp.Key;
                
                var guidMatch = System.Text.RegularExpressions.Regex.Match(fullVolumePath, @"\{([0-9a-fA-F\-]+)\}");
                if (guidMatch.Success)
                {
                    var extractedGuid = guidMatch.Groups[1].Value;
                    var normalizedFull = extractedGuid.Replace("-", "").ToLowerInvariant();
                    
                    if (normalizedFull == normalizedPartial)
                    {
                        return kvp.Value;
                    }
                    
                    if (normalizedFull.Contains(normalizedPartial))
                    {
                        return kvp.Value;
                    }
                    
                    if (normalizedPartial.Length >= 16)
                    {
                        var partialFirst16 = normalizedPartial.Substring(0, 16);
                        var partialLast8 = normalizedPartial.Length >= 24 ? normalizedPartial.Substring(16, 8) : "";
                        
                        if (normalizedFull.StartsWith(partialFirst16))
                        {
                            if (string.IsNullOrEmpty(partialLast8) || normalizedFull.Contains(partialLast8))
                            {
                                return kvp.Value;
                            }
                        }
                    }
                    
                    if (normalizedPartial.Length >= 8)
                    {
                        bool allCharsMatch = true;
                        int lastFoundIndex = -1;
                        
                        foreach (char c in normalizedPartial)
                        {
                            int foundIndex = normalizedFull.IndexOf(c, lastFoundIndex + 1);
                            if (foundIndex == -1)
                            {
                                allCharsMatch = false;
                                break;
                            }
                            lastFoundIndex = foundIndex;
                        }
                        
                        if (allCharsMatch)
                        {
                            return kvp.Value;
                        }
                    }
                }
            }

            return null;
        }

        public static void RefreshMappings()
        {
            VolumeMappings.Clear();
            VolumeGuidMappings.Clear();
            
            BuildDevicePathMappings();
            BuildVolumeGuidMappings();
        }
    }
}
