using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Drives.Core.Models;

namespace Drives.Core.Parsers
{
    /// <summary>
    /// Parses USB device information from Windows Registry (Live Registry Only)
    /// </summary>
    public static class UsbRegistryParser
    {
        // Windows API for getting registry key last write time
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryInfoKey(
            IntPtr hKey,
            StringBuilder lpClass,
            ref uint lpcClass,
            IntPtr lpReserved,
            out uint lpcSubKeys,
            out uint lpcMaxSubKeyLen,
            out uint lpcMaxClassLen,
            out uint lpcValues,
            out uint lpcMaxValueNameLen,
            out uint lpcMaxValueLen,
            out uint lpcbSecurityDescriptor,
            out long lpftLastWriteTime);

        /// <summary>
        /// Parse USB device history from live registry - ALL LOCATIONS
        /// </summary>
        public static List<UsbDeviceEntry> ParseUsbStorRegistry()
        {
            var entries = new List<UsbDeviceEntry>();

            try
            {
                // 1. HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceClasses\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}
                var deviceClassEntries = ParseDeviceClasses();
                entries.AddRange(deviceClassEntries);

                // 2. HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\USBSTOR
                var usbStorEntries = ParseUsbStor();
                entries.AddRange(usbStorEntries);

                // 3. HKEY_LOCAL_MACHINE\SYSTEM\MountedDevices
                var mountedDevicesEntries = ParseMountedDevices();
                entries.AddRange(mountedDevicesEntries);

                // 4. HKEY_USERS\{SID}\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2
                var mountPoints2Entries = ParseMountPoints2();
                entries.AddRange(mountPoints2Entries);

                // 5. HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Portable Devices
                var portableDevicesEntries = ParseWindowsPortableDevices();
                entries.AddRange(portableDevicesEntries);

                // 6. HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Search\VolumeInfoCache
                var volumeInfoCacheEntries = ParseVolumeInfoCache();
                entries.AddRange(volumeInfoCacheEntries);

                // 7. HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\SWD\WPDBUSENUM
                var wpdBusEnumEntries = ParseWpdBusEnum();
                entries.AddRange(wpdBusEnumEntries);
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseDeviceClasses()
        {
            var entries = new List<UsbDeviceEntry>();
            string keyPath = @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}";

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return entries;
                    }

                    var subKeyNames = key.GetSubKeyNames();

                    foreach (string subKeyName in subKeyNames)
                    {
                        try
                        {
                            if (subKeyName.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;

                                var entry = new UsbDeviceEntry
                                {
                                    Action = "Partition Con/Discon",
                                    Log = "Reg",
                                    Timestamp = GetRegistryKeyTimestamp(subKey)
                                };

                                // Parse the key name
                                // Example: ##?#USBSTOR#Disk&Ven_Intenso&Prod_Speed_Line&Rev_3.00#24080593020024&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}
                                var parts = subKeyName.Split('#');
                                
                                string vendor = "";
                                string product = "";
                                string serial = "";
                                string vguid = "";

                                // Find USBSTOR part
                                for (int i = 0; i < parts.Length; i++)
                                {
                                    if (parts[i].Equals("USBSTOR", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Next part has Disk&Ven_X&Prod_Y&Rev_Z
                                        if (i + 1 < parts.Length)
                                        {
                                            var deviceParts = parts[i + 1].Split('&');
                                            foreach (var p in deviceParts)
                                            {
                                                if (p.StartsWith("Ven_", StringComparison.OrdinalIgnoreCase))
                                                    vendor = p.Substring(4).Replace("_", " ");
                                                else if (p.StartsWith("Prod_", StringComparison.OrdinalIgnoreCase))
                                                    product = p.Substring(5).Replace("_", " ");
                                            }
                                        }
                                        
                                        // Serial is next part (remove &0 suffix)
                                        if (i + 2 < parts.Length)
                                        {
                                            serial = parts[i + 2].Split('&')[0];
                                        }
                                        
                                        // VGUID is the part after that
                                        if (i + 3 < parts.Length)
                                        {
                                            vguid = FormatVGUID(parts[i + 3]);
                                        }
                                        break;
                                    }
                                }

                                entry.DeviceName = $"{vendor} {product}".Trim();
                                entry.Serial = serial;
                                entry.VGUID = vguid;

                                entries.Add(entry);
                            }
                        }
                        catch (Exception)
                        {
                            // Skip problematic entries
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseUsbStor()
        {
            var entries = new List<UsbDeviceEntry>();
            string keyPath = @"SYSTEM\CurrentControlSet\Enum\USBSTOR";

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return entries;
                    }

                    var subKeyNames = key.GetSubKeyNames();

                    foreach (string deviceTypeKey in subKeyNames)
                    {
                        try
                        {
                            using (var deviceTypeSubKey = key.OpenSubKey(deviceTypeKey))
                            {
                                if (deviceTypeSubKey == null) continue;

                                // Parse vendor and product from device type key
                                // Example: Disk&Ven_Intenso&Prod_Speed_Line&Rev_3.00
                                string vendor = "";
                                string product = "";
                                var deviceParts = deviceTypeKey.Split('&');
                                foreach (var p in deviceParts)
                                {
                                    if (p.StartsWith("Ven_", StringComparison.OrdinalIgnoreCase))
                                        vendor = p.Substring(4).Replace("_", " ");
                                    else if (p.StartsWith("Prod_", StringComparison.OrdinalIgnoreCase))
                                        product = p.Substring(5).Replace("_", " ");
                                }

                                // Enumerate serial number subkeys
                                var serialSubKeys = deviceTypeSubKey.GetSubKeyNames();
                                foreach (string serialKey in serialSubKeys)
                                {
                                    using (var serialSubKey = deviceTypeSubKey.OpenSubKey(serialKey))
                                    {
                                        if (serialSubKey == null) continue;

                                        var entry = new UsbDeviceEntry
                                        {
                                            Action = "Device Connection",
                                            Log = "Reg",
                                            Timestamp = GetRegistryKeyTimestamp(serialSubKey)
                                        };

                                        // Remove &0 suffix from serial
                                        entry.Serial = serialKey.Split('&')[0];

                                        // Try to get FriendlyName and remove "USB Device" suffix
                                        var friendlyName = serialSubKey.GetValue("FriendlyName")?.ToString();
                                        if (!string.IsNullOrEmpty(friendlyName))
                                        {
                                            friendlyName = friendlyName.Replace("USB Device", "").Trim();
                                            entry.DeviceName = friendlyName;
                                        }
                                        else
                                        {
                                            entry.DeviceName = $"{vendor} {product}".Trim();
                                        }

                                        entries.Add(entry);
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Skip problematic entries
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseMountedDevices()
        {
            var entries = new List<UsbDeviceEntry>();
            string keyPath = @"SYSTEM\MountedDevices";

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return entries;
                    }

                    var valueNames = key.GetValueNames();

                    // Get key timestamp (same for all entries in this key)
                    var keyTimestamp = GetRegistryKeyTimestamp(key);

                    foreach (string valueName in valueNames)
                    {
                        try
                        {
                            var value = key.GetValue(valueName);
                            
                            string valueStr = "";
                            if (value is byte[] bytes)
                            {
                                valueStr = System.Text.Encoding.Unicode.GetString(bytes).Replace("\0", "");
                            }
                            else
                            {
                                valueStr = value?.ToString() ?? "";
                            }

                            if (valueStr.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) < 0) continue;

                            var entry = new UsbDeviceEntry
                            {
                                Action = "Drive Letter Assign",
                                Log = "Reg",
                                Timestamp = keyTimestamp
                            };

                            // Extract drive letter
                            if (valueName.Contains(":"))
                            {
                                var match = Regex.Match(valueName, @"([A-Z]:)");
                                if (match.Success)
                                    entry.Drive = match.Groups[1].Value;
                            }

                            // Parse device info from value
                            // Example: _??_USBSTOR#Disk&Ven_Intenso&Prod_Speed_Line&Rev_3.00#24080593020024&0#{53f56307-b6bf-11d0-94f2-00a0c91efb8b}
                            var parts = valueStr.Split(new[] { '#', '&' }, StringSplitOptions.RemoveEmptyEntries);
                            
                            string vendor = "";
                            string product = "";
                            string serial = "";
                            string vguid = "";

                            for (int i = 0; i < parts.Length; i++)
                            {
                                if (parts[i].StartsWith("Ven_", StringComparison.OrdinalIgnoreCase))
                                    vendor = parts[i].Substring(4).Replace("_", " ");
                                else if (parts[i].StartsWith("Prod_", StringComparison.OrdinalIgnoreCase))
                                    product = parts[i].Substring(5).Replace("_", " ");
                                else if (parts[i].StartsWith("{") && parts[i].EndsWith("}"))
                                    vguid = FormatVGUID(parts[i]);
                                else if (!parts[i].Equals("USBSTOR", StringComparison.OrdinalIgnoreCase) &&
                                        !parts[i].Equals("Disk", StringComparison.OrdinalIgnoreCase) &&
                                        !parts[i].StartsWith("Rev_", StringComparison.OrdinalIgnoreCase) &&
                                        !parts[i].StartsWith("_??_", StringComparison.OrdinalIgnoreCase) &&
                                        parts[i].Length > 5 &&
                                        !parts[i].StartsWith("Ven_") &&
                                        !parts[i].StartsWith("Prod_") &&
                                        parts[i] != "0")
                                {
                                    serial = parts[i];
                                }
                            }

                            entry.DeviceName = $"{vendor} {product}".Trim();
                            entry.Serial = serial;
                            entry.VGUID = vguid;

                            entries.Add(entry);
                        }
                        catch (Exception)
                        {
                            // Skip problematic entries
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseMountPoints2()
        {
            var entries = new List<UsbDeviceEntry>();

            try
            {
                using (var usersKey = Microsoft.Win32.Registry.Users)
                {
                    var userSids = usersKey.GetSubKeyNames();

                    foreach (var sid in userSids)
                    {
                        try
                        {
                            string keyPath = $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";

                            using (var key = usersKey.OpenSubKey(keyPath))
                            {
                                if (key == null) continue;

                                var subKeyNames = key.GetSubKeyNames();

                                foreach (string subKeyName in subKeyNames)
                                {
                                    // Process entries with GUID format {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
                                    if (subKeyName.StartsWith("{") && subKeyName.EndsWith("}"))
                                    {
                                        using (var subKey = key.OpenSubKey(subKeyName))
                                        {
                                            if (subKey == null) continue;

                                            var entry = new UsbDeviceEntry
                                            {
                                                Action = "Mount Point",
                                                Log = "Reg",
                                                VGUID = FormatVGUID(subKeyName),
                                                Timestamp = GetRegistryKeyTimestamp(subKey)
                                            };

                                            entries.Add(entry);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Skip problematic entries
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseWindowsPortableDevices()
        {
            var entries = new List<UsbDeviceEntry>();
            string keyPath = @"SOFTWARE\Microsoft\Windows Portable Devices\Devices";

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return entries;
                    }

                    var subKeyNames = key.GetSubKeyNames();

                    foreach (string subKeyName in subKeyNames)
                    {
                        if (subKeyName.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        try
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;

                                var entry = new UsbDeviceEntry
                                {
                                    Action = "Connection",
                                    Log = "Reg",
                                    Timestamp = GetRegistryKeyTimestamp(subKey)
                                };

                                // Parse subkey name
                                // Example: SWD#WPDBUSENUM#_??_USBSTOR#DISK&VEN_INTENSO&PROD_SPEED_LINE&REV_3.00#24080593020024&0#{53F56307-B6BF-11D0-94F2-00A0C91EFB8B}
                                var parts = subKeyName.Split(new[] { '#', '&' }, StringSplitOptions.RemoveEmptyEntries);
                                
                                string vendor = "";
                                string product = "";
                                string serial = "";
                                string vguid = "";

                                for (int i = 0; i < parts.Length; i++)
                                {
                                    if (parts[i].StartsWith("VEN_", StringComparison.OrdinalIgnoreCase))
                                        vendor = parts[i].Substring(4).Replace("_", " ");
                                    else if (parts[i].StartsWith("PROD_", StringComparison.OrdinalIgnoreCase))
                                        product = parts[i].Substring(5).Replace("_", " ");
                                    else if (parts[i].StartsWith("{") && parts[i].EndsWith("}"))
                                        vguid = FormatVGUID(parts[i]);
                                    else if (!parts[i].Equals("SWD", StringComparison.OrdinalIgnoreCase) &&
                                            !parts[i].Equals("WPDBUSENUM", StringComparison.OrdinalIgnoreCase) &&
                                            !parts[i].Equals("USBSTOR", StringComparison.OrdinalIgnoreCase) &&
                                            !parts[i].Equals("DISK", StringComparison.OrdinalIgnoreCase) &&
                                            !parts[i].StartsWith("REV_", StringComparison.OrdinalIgnoreCase) &&
                                            !parts[i].StartsWith("_??_", StringComparison.OrdinalIgnoreCase) &&
                                            !parts[i].StartsWith("VEN_") &&
                                            !parts[i].StartsWith("PROD_") &&
                                            parts[i].Length > 5 &&
                                            parts[i] != "0")
                                    {
                                        serial = parts[i];
                                    }
                                }

                                entry.DeviceName = $"{vendor} {product}".Trim();
                                entry.Serial = serial;
                                entry.VGUID = vguid;

                                entries.Add(entry);
                            }
                        }
                        catch (Exception)
                        {
                            // Skip problematic entries
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseVolumeInfoCache()
        {
            var entries = new List<UsbDeviceEntry>();
            string keyPath = @"SOFTWARE\Microsoft\Windows Search\VolumeInfoCache";

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return entries;
                    }

                    var subKeyNames = key.GetSubKeyNames();

                    foreach (string subKeyName in subKeyNames)
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;

                                var entry = new UsbDeviceEntry
                                {
                                    Action = "Volume Cache",
                                    Log = "Reg",
                                    Drive = subKeyName, // Drive letter like "C:", "D:", etc.
                                    Timestamp = GetRegistryKeyTimestamp(subKey)
                                };

                                // Try to get volume label
                                var volumeLabel = subKey.GetValue("VolumeLabel")?.ToString();
                                if (!string.IsNullOrEmpty(volumeLabel))
                                {
                                    // Check if the label contains a Volume GUID path
                                    // Pattern: \\?\Volume{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}\
                                    if (volumeLabel.Contains("Volume{") && volumeLabel.Contains("}"))
                                    {
                                        // Extract GUID from Volume path
                                        var guidMatch = Regex.Match(volumeLabel, @"\{([0-9A-Fa-f\-]+)\}");
                                        if (guidMatch.Success)
                                        {
                                            // Put GUID in VGUID column, leave Label empty
                                            entry.VGUID = FormatVGUID(guidMatch.Groups[1].Value);
                                        }
                                        else
                                        {
                                            // Fallback: put raw value in label
                                            entry.Label = volumeLabel;
                                        }
                                    }
                                    else
                                    {
                                        // Regular label, not a Volume GUID path
                                        entry.Label = volumeLabel;
                                    }
                                }

                                entries.Add(entry);
                            }
                        }
                        catch (Exception)
                        {
                            // Skip problematic entries
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        private static List<UsbDeviceEntry> ParseWpdBusEnum()
        {
            var entries = new List<UsbDeviceEntry>();
            string keyPath = @"SYSTEM\CurrentControlSet\Enum\SWD\WPDBUSENUM";

            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return entries;
                    }

                    var subKeyNames = key.GetSubKeyNames();

                    foreach (string subKeyName in subKeyNames)
                    {
                        try
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;

                                var entry = new UsbDeviceEntry
                                {
                                    Action = "Device Enumeration",
                                    Log = "Reg",
                                    Timestamp = GetRegistryKeyTimestamp(subKey)
                                };

                                // Parse subkey name
                                // Example: {GUID}#description or just description
                                var parts = subKeyName.Split(new[] { '#', '&' }, StringSplitOptions.RemoveEmptyEntries);

                                string vendor = "";
                                string product = "";
                                string vguid = "";

                                for (int i = 0; i < parts.Length; i++)
                                {
                                    if (parts[i].StartsWith("VEN_", StringComparison.OrdinalIgnoreCase))
                                        vendor = parts[i].Substring(4).Replace("_", " ");
                                    else if (parts[i].StartsWith("PROD_", StringComparison.OrdinalIgnoreCase))
                                        product = parts[i].Substring(5).Replace("_", " ");
                                    else if (parts[i].StartsWith("{") && parts[i].EndsWith("}"))
                                        vguid = FormatVGUID(parts[i]);
                                }

                                entry.DeviceName = $"{vendor} {product}".Trim();
                                entry.VGUID = vguid;

                                entries.Add(entry);
                            }
                        }
                        catch (Exception)
                        {
                            // Skip problematic entries
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        /// <summary>
        /// Get the last write time of a registry key
        /// </summary>
        private static DateTime GetRegistryKeyTimestamp(RegistryKey key)
        {
            try
            {
                var hKey = key.Handle.DangerousGetHandle();
                uint classSize = 0;
                if (RegQueryInfoKey(
                    hKey,
                    null,
                    ref classSize,
                    IntPtr.Zero,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out long filetime) == 0)
                {
                    return DateTime.FromFileTime(filetime);
                }
            }
            catch (Exception)
            {
                // Return null timestamp if unable to get
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Format GUID string with curly braces
        /// </summary>
        private static string FormatVGUID(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return "";

            guid = guid.Trim();
            if (!guid.StartsWith("{"))
                guid = "{" + guid;
            if (!guid.EndsWith("}"))
                guid = guid + "}";

            return guid.ToLowerInvariant();
        }

        /// <summary>
        /// Get the install date of a device from its Device ID
        /// </summary>
        public static DateTime GetDeviceInstallDate(string deviceId)
        {
            try
            {
                string keyPath = $@"SYSTEM\CurrentControlSet\Enum\{deviceId}";
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        return GetRegistryKeyTimestamp(key);
                    }
                }
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return DateTime.MinValue;
        }
    }
}
