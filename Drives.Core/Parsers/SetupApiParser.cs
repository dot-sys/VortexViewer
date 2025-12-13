using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Drives.Core.Models;

namespace Drives.Core.Parsers
{
    /// <summary>
    /// Parses SetupAPI device log files for USB device installation events
    /// </summary>
    public static class SetupApiParser
    {
        /// <summary>
        /// Parse setupapi.dev*.log files
        /// </summary>
        public static List<UsbDeviceEntry> ParseSetupApiLogs()
        {
            var entries = new List<UsbDeviceEntry>();

            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf");
                string[] logFiles = Directory.GetFiles(logPath, "setupapi.dev*.log");

                foreach (string logFile in logFiles)
                {
                    try
                    {
                        var fileEntries = ParseSetupApiLogFile(logFile);
                        entries.AddRange(fileEntries);
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }
            }
            catch
            {
                // Failed to access log directory
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseSetupApiLogFile(string filePath)
        {
            var entries = new List<UsbDeviceEntry>();
            DateTime? currentTimestamp = null;

            try
            {
                var lines = File.ReadAllLines(filePath);

                foreach (var line in lines)
                {
                    try
                    {
                        // Check for timestamp line: ">>>  Section start 2025/01/18 23:33:50.123"
                        var timestampMatch = Regex.Match(line, @">>>\s+Section start (.*)");
                        if (timestampMatch.Success)
                        {
                            string rawTimestamp = timestampMatch.Groups[1].Value.Split('.')[0];
                            if (DateTime.TryParseExact(rawTimestamp, "yyyy/MM/dd HH:mm:ss", 
                                null, System.Globalization.DateTimeStyles.None, out DateTime timestamp))
                            {
                                currentTimestamp = timestamp;
                            }
                            continue;
                        }

                        // Check for device line: ">>>  [USBSTOR\...]" or ">>>  [...USBSTOR\...]"
                        var deviceMatch = Regex.Match(line, @">>>\s+\[.*?((?:USBSTOR|STORAGE\\VOLUME|SWD\\WPDBUSENUM)\\[^\]]+)\]");
                        if (deviceMatch.Success && currentTimestamp.HasValue)
                        {
                            string deviceId = deviceMatch.Groups[1].Value;

                            // Parse device information based on format
                            string vendor = "";
                            string product = "";
                            string serial = "";
                            string vguid = "";
                            string drive = "";

                            // Handle STORAGE\VOLUME format (e.g., STORAGE\VOLUME\_??_USBSTOR#...)
                            if (deviceId.IndexOf("STORAGE\\VOLUME", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Extract VGUID from STORAGE\VOLUME path
                                var guidMatch = Regex.Match(deviceId, @"\{([0-9a-fA-F-]+)\}", RegexOptions.IgnoreCase);
                                if (guidMatch.Success)
                                {
                                    vguid = "{" + guidMatch.Groups[1].Value.ToLower() + "}";
                                }

                                // Extract embedded USBSTOR information from STORAGE\VOLUME path
                                // Pattern handles both USBSTOR#...#...# and USBSTOR#...#...#{GUID}
                                var usbstorMatch = Regex.Match(deviceId, @"USBSTOR#([^#]+)#([^#]+)(?:#|\#{)", RegexOptions.IgnoreCase);
                                if (usbstorMatch.Success)
                                {
                                    string deviceInfo = usbstorMatch.Groups[1].Value;
                                    serial = usbstorMatch.Groups[2].Value.Replace("&0", "").Replace("&1", "");
                                    
                                    ParseVendorProduct(deviceInfo, out vendor, out product);
                                }
                            }
                            // Handle SWD\WPDBUSENUM format (e.g., SWD\WPDBUSENUM\_??_USBSTOR#...)
                            else if (deviceId.IndexOf("SWD\\WPDBUSENUM", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Extract VGUID
                                var guidMatch = Regex.Match(deviceId, @"\{([0-9a-fA-F-]+)\}", RegexOptions.IgnoreCase);
                                if (guidMatch.Success)
                                {
                                    vguid = "{" + guidMatch.Groups[1].Value.ToLower() + "}";
                                }

                                // Extract embedded USBSTOR information
                                // Pattern handles both USBSTOR#...#...# and USBSTOR#...#...#{GUID}
                                var usbstorMatch = Regex.Match(deviceId, @"USBSTOR#([^#]+)#([^#]+)(?:#|\#{)", RegexOptions.IgnoreCase);
                                if (usbstorMatch.Success)
                                {
                                    string deviceInfo = usbstorMatch.Groups[1].Value;
                                    serial = usbstorMatch.Groups[2].Value.Replace("&0", "").Replace("&1", "");
                                    
                                    ParseVendorProduct(deviceInfo, out vendor, out product);
                                }
                            }
                            // Handle USBSTOR format (e.g., USBSTOR\DISK&VEN_...&PROD_...\SERIAL&0)
                            else if (deviceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase))
                            {
                                // Split by backslash to get parts
                                var parts = deviceId.Split('\\');
                                
                                if (parts.Length >= 2)
                                {
                                    // First part after USBSTOR contains vendor and product
                                    string deviceInfo = parts[1];
                                    ParseVendorProduct(deviceInfo, out vendor, out product);
                                }
                                
                                if (parts.Length >= 3)
                                {
                                    // Second part contains serial number (possibly with &0 suffix)
                                    serial = parts[2].Split('&')[0].Replace("&0", "").Replace("&1", "");
                                }
                            }

                            // Only add entry if we extracted meaningful information
                            if (!string.IsNullOrEmpty(vendor) || !string.IsNullOrEmpty(product) || !string.IsNullOrEmpty(serial))
                            {
                                entries.Add(new UsbDeviceEntry
                                {
                                    Timestamp = currentTimestamp.Value,
                                    DeviceName = $"{vendor} {product}".Trim(),
                                    Serial = serial,
                                    VGUID = vguid,
                                    Drive = drive,
                                    Action = "Partition Con/Discon",
                                    Log = "SAPI"
                                });
                            }
                        }
                    }
                    catch
                    {
                        // Skip problematic lines
                    }
                }
            }
            catch
            {
                // Failed to read file
            }

            return entries;
        }

        /// <summary>
        /// Parse vendor and product from device info string
        /// </summary>
        private static void ParseVendorProduct(string deviceInfo, out string vendor, out string product)
        {
            vendor = "";
            product = "";
            
            var vendorMatch = Regex.Match(deviceInfo, @"(?:Ven|VEN)_([^&]+)", RegexOptions.IgnoreCase);
            if (vendorMatch.Success)
            {
                vendor = vendorMatch.Groups[1].Value.Replace("_", " ");
            }

            var productMatch = Regex.Match(deviceInfo, @"(?:Prod|PROD)_([^&]+)", RegexOptions.IgnoreCase);
            if (productMatch.Success)
            {
                product = productMatch.Groups[1].Value.Replace("_", " ");
            }
        }
    }
}
