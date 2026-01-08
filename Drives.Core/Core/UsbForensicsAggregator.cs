using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Drives.Core.Models;
using Drives.Core.Parsers;

// Combines data from all USB sources
namespace Drives.Core.Core
{
    // Merges and enriches USB artifacts
    public static class UsbForensicsAggregator
    {
        public static UsbForensicsResult CollectUsbForensics()
        {
            var allEntries = new List<UsbDeviceEntry>();

            try
            {
                var setupApiEntries = SetupApiParser.ParseSetupApiLogs();
                allEntries.AddRange(setupApiEntries);
            }
            catch (Exception)
            {
            }

            try
            {
                var pnpEntries = PnpDeviceParser.ParsePnpDevices();
                allEntries.AddRange(pnpEntries);
            }
            catch (Exception)
            {
            }

            try
            {
                var eventLogEntries = UsbEventLogParser.ParseEventLogs();
                allEntries.AddRange(eventLogEntries);
            }
            catch (Exception)
            {
            }

            try
            {
                var registryEntries = ParseRegistryHives();
                foreach (var entry in registryEntries)
                {
                }
                allEntries.AddRange(registryEntries);
            }
            catch (Exception)
            {
            }

            
            // Count entries by log type
            var logTypeCounts = allEntries.GroupBy(e => e.Log).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kvp in logTypeCounts)
            {
            }

            // Get volume information
            List<VolumeInfo> volumes = new List<VolumeInfo>();
            try
            {
                volumes = VolumeInfoParser.GetVolumeInformation();
            }
            catch (Exception)
            {
            }

            // Enrich entries with volume information
            EnrichWithVolumeInfo(allEntries, volumes);

            // Fill in missing information across entries
            FillMissingInformation(allEntries, volumes);

            // Clean up and normalize data
            CleanupData(allEntries);

            // Remove duplicates and sort
            var uniqueEntries = RemoveDuplicates(allEntries);
            uniqueEntries = uniqueEntries.OrderByDescending(e => e.Timestamp).ToList();


            return new UsbForensicsResult(uniqueEntries, volumes);
        }

        private static List<UsbDeviceEntry> ParseRegistryHives()
        {
            var entries = new List<UsbDeviceEntry>();

            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                var pnpEntries = UsbRegistryParser.ParseUsbStorRegistry();
                
                foreach (var entry in pnpEntries)
                {
                }
                
                entries.AddRange(pnpEntries);
                
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            catch (Exception)
            {
            }

            return entries;
        }

        // Enriches entries with currently connected volume data
        private static void EnrichWithVolumeInfo(List<UsbDeviceEntry> entries, List<VolumeInfo> volumes)
        {
            // Create lookup dictionaries
            var serialLookup = volumes
                .Where(v => !string.IsNullOrEmpty(v.Serial))
                .GroupBy(v => v.Serial.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.First());

            var driveLookup = volumes
                .Where(v => !string.IsNullOrEmpty(v.Drive))
                .ToDictionary(v => v.Drive.ToUpper() + ":", v => v);

            foreach (var entry in entries)
            {
                VolumeInfo volumeSource = null;

                // Try to match by serial number
                if (!string.IsNullOrEmpty(entry.Serial))
                {
                    string serialKey = entry.Serial.Trim().ToUpper();
                    serialLookup.TryGetValue(serialKey, out volumeSource);
                }

                // Try to match by drive letter
                if (volumeSource == null && !string.IsNullOrEmpty(entry.Drive))
                {
                    string driveKey = entry.Drive.Split(':')[0].Trim().ToUpper() + ":";
                    driveLookup.TryGetValue(driveKey, out volumeSource);
                }

                // Enrich entry with volume information
                if (volumeSource != null)
                {
                    if (string.IsNullOrEmpty(entry.Serial) && !string.IsNullOrEmpty(volumeSource.Serial))
                    {
                        entry.Serial = volumeSource.Serial.Trim();
                    }

                    if (string.IsNullOrEmpty(entry.Drive) && !string.IsNullOrEmpty(volumeSource.Drive))
                    {
                        entry.Drive = volumeSource.Drive + @":\";
                    }

                    if (string.IsNullOrEmpty(entry.VGUID) && !string.IsNullOrEmpty(volumeSource.PartitionID))
                    {
                        entry.VGUID = volumeSource.PartitionID.ToLower();
                    }
                    
                    // Only enrich label when serials match
                    if (string.IsNullOrEmpty(entry.Label) && !string.IsNullOrEmpty(volumeSource.Label))
                    {
                        // Verify serial numbers match before enriching label
                        if (!string.IsNullOrEmpty(entry.Serial) && 
                            !string.IsNullOrEmpty(volumeSource.Serial) &&
                            entry.Serial.Trim().Equals(volumeSource.Serial.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Label = volumeSource.Label.Trim();
                        }
                    }

                    // Enrich BusType from volume information
                    if (string.IsNullOrEmpty(entry.BusType) && !string.IsNullOrEmpty(volumeSource.BusType))
                    {
                        entry.BusType = volumeSource.BusType;
                    }
                    
                    // NEW: Enrich DeviceName from volume label if needed
                    if (string.IsNullOrEmpty(entry.DeviceName) && !string.IsNullOrEmpty(volumeSource.Label))
                    {
                        // Only use label as DeviceName if it's not "Local Disk" (generic label)
                        if (!volumeSource.Label.Equals("Local Disk", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.DeviceName = volumeSource.Label.Trim();
                        }
                    }

                    // Skip further filling - current volume data is authoritative
                    continue;
                }
            }
        }

        // Cross-references to fill missing fields
        private static void FillMissingInformation(List<UsbDeviceEntry> entries, List<VolumeInfo> volumes)
        {
            // VGUID is primary key for partitions
            var volumeByVGUID = volumes
                .Where(v => !string.IsNullOrEmpty(v.PartitionID))
                .ToDictionary(v => v.PartitionID.Trim().ToLower(), v => v);

            // Serial is secondary key for disks
            var volumesBySerial = volumes
                .Where(v => !string.IsNullOrEmpty(v.Serial))
                .GroupBy(v => v.Serial.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.ToList());

            var serialGroups = entries
                .Where(e => !string.IsNullOrEmpty(e.Serial))
                .GroupBy(e => e.Serial.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.ToList());

            var vguidGroups = entries
                .Where(e => !string.IsNullOrEmpty(e.VGUID))
                .GroupBy(e => e.VGUID.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.ToList());
            
            // Fill in missing fields - PRIORITIZE CURRENTLY CONNECTED VOLUMES FIRST
            foreach (var entry in entries)
            {
                VolumeInfo currentVolume = null;

                // Step 1: Try to find currently connected volume by VGUID (PRIMARY - most specific)
                if (!string.IsNullOrEmpty(entry.VGUID))
                {
                    string vguidKey = entry.VGUID.Trim().ToLower();
                    volumeByVGUID.TryGetValue(vguidKey, out currentVolume);
                }

                // Step 2: If not found by VGUID, try Serial (SECONDARY - less specific, shared by partitions)
                // BUT ONLY if we don't have a VGUID (avoid confusion with virtual partitions)
                if (currentVolume == null && !string.IsNullOrEmpty(entry.Serial) && string.IsNullOrEmpty(entry.VGUID))
                {
                    string serialKey = entry.Serial.Trim().ToUpper();
                    if (volumesBySerial.TryGetValue(serialKey, out var volumeList))
                    {
                        // If multiple partitions match the serial, we can't determine which one
                        // Only use if there's exactly one match
                        if (volumeList.Count == 1)
                        {
                            currentVolume = volumeList[0];
                        }
                    }
                }

                // Step 3: PRIORITIZE filling from currently connected volume
                if (currentVolume != null)
                {
                    // Fill ALL missing fields from currently connected volume (HIGHEST PRIORITY)
                    if (string.IsNullOrEmpty(entry.Drive) && !string.IsNullOrEmpty(currentVolume.Drive))
                    {
                        entry.Drive = currentVolume.Drive + @":\";
                    }

                    if (string.IsNullOrEmpty(entry.Label) && !string.IsNullOrEmpty(currentVolume.Label))
                    {
                        entry.Label = currentVolume.Label.Trim();
                    }

                    if (string.IsNullOrEmpty(entry.BusType) && !string.IsNullOrEmpty(currentVolume.BusType))
                    {
                        entry.BusType = currentVolume.BusType;
                    }

                    if (string.IsNullOrEmpty(entry.VGUID) && !string.IsNullOrEmpty(currentVolume.PartitionID))
                    {
                        entry.VGUID = currentVolume.PartitionID.ToLower();
                    }

                    if (string.IsNullOrEmpty(entry.Serial) && !string.IsNullOrEmpty(currentVolume.Serial))
                    {
                        entry.Serial = currentVolume.Serial.Trim();
                    }

                    // Skip further filling - current volume data is authoritative
                    continue;
                }

                // Step 4: Only if NOT found in currently connected volumes, use related log entries
                List<UsbDeviceEntry> relatedEntries = new List<UsbDeviceEntry>();

                // Find related entries by serial
                if (!string.IsNullOrEmpty(entry.Serial))
                {
                    string serialKey = entry.Serial.Trim().ToUpper();
                    if (serialGroups.ContainsKey(serialKey))
                    {
                        relatedEntries.AddRange(serialGroups[serialKey]);
                    }
                }

                // Find related entries by VGUID
                if (!string.IsNullOrEmpty(entry.VGUID))
                {
                    string vguidKey = entry.VGUID.Trim().ToLower();
                    if (vguidGroups.ContainsKey(vguidKey))
                    {
                        relatedEntries.AddRange(vguidGroups[vguidKey]);
                    }
                }
                
                // Fill from related entries
                foreach (var related in relatedEntries.Distinct())
                {
                    if (string.IsNullOrEmpty(entry.DeviceName) && !string.IsNullOrEmpty(related.DeviceName))
                    {
                        entry.DeviceName = related.DeviceName;
                    }

                    // Only fill label when identifiers match
                    if (string.IsNullOrEmpty(entry.Label) && !string.IsNullOrEmpty(related.Label))
                    {
                        // Prefer VGUID match (most specific)
                        bool vguidMatch = !string.IsNullOrEmpty(entry.VGUID) && 
                                         !string.IsNullOrEmpty(related.VGUID) &&
                                         entry.VGUID.Trim().Equals(related.VGUID.Trim(), StringComparison.OrdinalIgnoreCase);
                        
                        // Fall back to serial match (less specific)
                        bool serialMatch = !string.IsNullOrEmpty(entry.Serial) && 
                                          !string.IsNullOrEmpty(related.Serial) &&
                                          entry.Serial.Trim().Equals(related.Serial.Trim(), StringComparison.OrdinalIgnoreCase);
                        
                        if (vguidMatch || serialMatch)
                        {
                            entry.Label = related.Label;
                        }
                    }

                    if (string.IsNullOrEmpty(entry.Drive) && !string.IsNullOrEmpty(related.Drive))
                    {
                        entry.Drive = related.Drive;
                    }

                    if (string.IsNullOrEmpty(entry.Serial) && !string.IsNullOrEmpty(related.Serial))
                    {
                        entry.Serial = related.Serial;
                    }

                    if (string.IsNullOrEmpty(entry.VGUID) && !string.IsNullOrEmpty(related.VGUID))
                    {
                        entry.VGUID = related.VGUID;
                    }

                    if (string.IsNullOrEmpty(entry.BusType) && !string.IsNullOrEmpty(related.BusType))
                    {
                        entry.BusType = related.BusType;
                    }
                }
            }

            // Final pass for labels from connected volumes
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Label))
                {
                    VolumeInfo matchingVolume = null;
                    
                    // Try VGUID match first (most specific)
                    if (!string.IsNullOrEmpty(entry.VGUID))
                    {
                        string vguidKey = entry.VGUID.Trim().ToLower();
                        volumeByVGUID.TryGetValue(vguidKey, out matchingVolume);
                    }
                    
                    // Try serial match only if VGUID didn't match and there's exactly one volume with this serial
                    if (matchingVolume == null && !string.IsNullOrEmpty(entry.Serial))
                    {
                        string serialKey = entry.Serial.Trim().ToUpper();
                        if (volumesBySerial.TryGetValue(serialKey, out var volumeList) && volumeList.Count == 1)
                        {
                            matchingVolume = volumeList[0];
                        }
                    }
                    
                    if (matchingVolume != null && !string.IsNullOrEmpty(matchingVolume.Label))
                    {
                        entry.Label = matchingVolume.Label.Trim();
                    }
                }
            }

            // Mark virtual drives
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Serial) && 
                    !string.IsNullOrEmpty(entry.VGUID) && 
                    !string.IsNullOrEmpty(entry.Drive))
                {
                    entry.DeviceName = "(Virtual Drive)";
                }
            }
        }

        private static void CleanupData(List<UsbDeviceEntry> entries)
        {
            var textInfo = CultureInfo.CurrentCulture.TextInfo;

            foreach (var entry in entries)
            {
                // Normalize drive letter format
                if (!string.IsNullOrEmpty(entry.Drive))
                {
                    entry.Drive = Regex.Replace(entry.Drive, @"^([A-Za-z])\s*\.?\s*.*", "${1}:\\");
                }

                // Normalize VGUID to lowercase
                if (!string.IsNullOrEmpty(entry.VGUID))
                {
                    entry.VGUID = entry.VGUID.ToLower();
                }

                // Clean device name
                if (!string.IsNullOrEmpty(entry.DeviceName))
                {
                    // Convert all-caps to title case
                    if (Regex.IsMatch(entry.DeviceName, "^[A-Z\\s]+$"))
                    {
                        entry.DeviceName = textInfo.ToTitleCase(entry.DeviceName.ToLower());
                    }

                    // Remove "Null" or "NULL" from device name (case-insensitive)
                    entry.DeviceName = Regex.Replace(entry.DeviceName, @"\bNull\b", "", RegexOptions.IgnoreCase).Trim();
                }

                // Clean label - DON'T convert to title case, keep original
                if (!string.IsNullOrEmpty(entry.Label))
                {
                    // Only trim, don't change case
                    entry.Label = entry.Label.Trim();
                }

                // Clean action
                if (!string.IsNullOrEmpty(entry.Action))
                {
                    entry.Action = Regex.Replace(entry.Action, @"^\((.*)\)$", "$1");
                    entry.Action = Regex.Replace(entry.Action, @"\s+", " ").Trim();
                }
            }
        }

        private static List<UsbDeviceEntry> RemoveDuplicates(List<UsbDeviceEntry> entries)
        {
            var seen = new HashSet<string>();
            var uniqueEntries = new List<UsbDeviceEntry>();

            foreach (var entry in entries)
            {
                // Create a unique key for each entry
                string key = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}|{entry.DeviceName}|{entry.Serial}|{entry.VGUID}|{entry.Action}|{entry.Log}";
                
                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    uniqueEntries.Add(entry);
                }
            }

            return uniqueEntries;
        }
    }
}
