using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Drives.Core.Models;

namespace Drives.Core.Parsers
{
    /// <summary>
    /// Parses USB device events from Windows Event Logs
    /// </summary>
    public static class UsbEventLogParser
    {
        /// <summary>
        /// Parse USB-related events from Event Logs
        /// </summary>
        public static List<UsbDeviceEntry> ParseEventLogs()
        {
            var entries = new List<UsbDeviceEntry>();

            try
            {
                // Parse Storage ClassPnP events (Event ID 507, 500, 523)
                var storageEntries = ParseStorageClassPnpEvents();
                entries.AddRange(storageEntries);

                // Parse Partition/Diagnostic events (Event ID 1006)
                var partitionEntries = ParsePartitionDiagnosticEvents();
                entries.AddRange(partitionEntries);

                // Parse Kernel-PnP Device Configuration Events (400, 410, 420, 430)
                var kernelConfigEntries = ParseKernelPnpDeviceConfigEvents();
                entries.AddRange(kernelConfigEntries);

                // Parse StorageVolume Operational Events (1001, 1002)
                var storageVolumeEntries = ParseStorageVolumeOperationalEvents();
                entries.AddRange(storageVolumeEntries);
            }
            catch (Exception)
            {
                // Silently handle errors
            }

            return entries;
        }

        /// <summary>
        /// Parse Microsoft-Windows-Storage-ClassPnP/Operational log for Event ID 507, 500, 523
        /// </summary>
        private static List<UsbDeviceEntry> ParseStorageClassPnpEvents()
        {
            var entries = new List<UsbDeviceEntry>();
            string logName = "Microsoft-Windows-Storage-ClassPnP/Operational";

            try
            {
                string query = "*[System[(EventID=507 or EventID=500 or EventID=523)]]";
                var eventQuery = new EventLogQuery(logName, PathType.LogName, query);

                using (var eventReader = new EventLogReader(eventQuery))
                {
                    EventRecord eventRecord;

                    while ((eventRecord = eventReader.ReadEvent()) != null)
                    {
                        using (eventRecord)
                        {
                            try
                            {
                                var entry = ParseStorageClassPnpEvent(eventRecord);
                                if (entry != null)
                                {
                                    entries.Add(entry);
                                }
                            }
                            catch
                            {
                                // Skip problematic entries
                            }
                        }
                    }
                }
            }
            catch
            {
                // Log not found or access denied
            }

            return entries;
        }

        /// <summary>
        /// Parse Microsoft-Windows-Partition/Diagnostic log for Event ID 1006
        /// </summary>
        private static List<UsbDeviceEntry> ParsePartitionDiagnosticEvents()
        {
            var entries = new List<UsbDeviceEntry>();
            string logName = "Microsoft-Windows-Partition/Diagnostic";

            try
            {
                string query = "*[System[(EventID=1006)]]";
                var eventQuery = new EventLogQuery(logName, PathType.LogName, query);

                using (var eventReader = new EventLogReader(eventQuery))
                {
                    EventRecord eventRecord;

                    while ((eventRecord = eventReader.ReadEvent()) != null)
                    {
                        using (eventRecord)
                        {
                            try
                            {
                                var entry = ParsePartitionDiagnosticEvent(eventRecord);
                                if (entry != null)
                                {
                                    entries.Add(entry);
                                }
                            }
                            catch
                            {
                                // Skip problematic entries
                            }
                        }
                    }
                }
            }
            catch
            {
                // Log not found or access denied
            }

            return entries;
        }

        /// <summary>
        /// Parse Microsoft-Windows-Kernel-PnP/Device Configuration Events (400, 410, 420, 430)
        /// </summary>
        private static List<UsbDeviceEntry> ParseKernelPnpDeviceConfigEvents()
        {
            var entries = new List<UsbDeviceEntry>();
            string logName = @"Microsoft-Windows-Kernel-PnP/Device Configuration";

            try
            {
                string query = "*[System[(EventID=400 or EventID=410 or EventID=420 or EventID=430)]]";
                var eventQuery = new EventLogQuery(logName, PathType.LogName, query);

                using (var eventReader = new EventLogReader(eventQuery))
                {
                    EventRecord eventRecord;

                    while ((eventRecord = eventReader.ReadEvent()) != null)
                    {
                        using (eventRecord)
                        {
                            try
                            {
                                var entry = ParseKernelPnpDeviceConfigEvent(eventRecord);
                                if (entry != null)
                                {
                                    entries.Add(entry);
                                }
                            }
                            catch
                            {
                                // Skip problematic entries
                            }
                        }
                    }
                }
            }
            catch
            {
                // Log not found or access denied
            }

            return entries;
        }

        private static UsbDeviceEntry ParseStorageClassPnpEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new UsbDeviceEntry
                {
                    Action = "Data Operation",
                    Log = "Evtx"
                };

                // Extract Event ID
                var eventIdElement = xdoc.Descendants(ns + "EventID").FirstOrDefault();
                int eventId = eventIdElement != null ? int.Parse(eventIdElement.Value) : 0;

                // Extract timestamp
                var timeCreatedElement = xdoc.Descendants(ns + "TimeCreated").FirstOrDefault();
                if (timeCreatedElement != null)
                {
                    var systemTimeAttr = timeCreatedElement.Attribute("SystemTime");
                    if (systemTimeAttr != null)
                    {
                        if (DateTime.TryParse(systemTimeAttr.Value, null, DateTimeStyles.RoundtripKind, out DateTime parsedTime))
                        {
                            entry.Timestamp = parsedTime.ToLocalTime();
                        }
                    }
                }

                // Check for DownLevelIrpStatus = 0xc000000e
                var downLevelIrpStatusElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "DownLevelIrpStatus");
                
                if (downLevelIrpStatusElement != null)
                {
                    string statusValue = downLevelIrpStatusElement.Value?.Trim() ?? "";
                    // Check if status is 0xc000000e (case-insensitive)
                    if (string.Equals(statusValue, "0xc000000e", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Action = "Device Unresponsive";
                    }
                }

                // Extract Vendor and Model
                var vendorElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "Vendor");
                var modelElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "Model");

                string vendor = vendorElement?.Value?.Trim() ?? "";
                string model = modelElement?.Value?.Trim() ?? "";

                entry.DeviceName = $"{vendor} {model}".Trim();

                // Extract Serial Number
                var serialElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "SerialNumber");
                entry.Serial = serialElement?.Value?.Trim() ?? "";

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static UsbDeviceEntry ParsePartitionDiagnosticEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new UsbDeviceEntry
                {
                    Action = "Partition Initialized",
                    Log = "Evtx"
                };

                // Extract timestamp from TimeCreated SystemTime
                var timeCreatedElement = xdoc.Descendants(ns + "TimeCreated").FirstOrDefault();
                if (timeCreatedElement != null)
                {
                    var systemTimeAttr = timeCreatedElement.Attribute("SystemTime");
                    if (systemTimeAttr != null)
                    {
                        if (DateTime.TryParse(systemTimeAttr.Value, null, DateTimeStyles.RoundtripKind, out DateTime parsedTime))
                        {
                            entry.Timestamp = parsedTime.ToLocalTime();
                        }
                    }
                }

                // Extract EventData fields
                var eventDataElements = xdoc.Descendants(ns + "EventData").FirstOrDefault();
                if (eventDataElements != null)
                {
                    // Extract Manufacturer and Model
                    var manufacturer = eventDataElements.Elements(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "Manufacturer")?.Value ?? "";
                    var model = eventDataElements.Elements(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "Model")?.Value ?? "";

                    // Combine Manufacturer and Model for DeviceName
                    entry.DeviceName = $"{manufacturer} {model}".Trim();

                    // Extract SerialNumber
                    entry.Serial = eventDataElements.Elements(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "SerialNumber")?.Value ?? "";

                    // Extract ParentId to check for serial number pattern
                    var parentId = eventDataElements.Elements(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "ParentId")?.Value ?? "";

                    // If SerialNumber is empty and ParentId contains USB\VID_ pattern, extract serial from ParentId
                    if (string.IsNullOrEmpty(entry.Serial) && !string.IsNullOrEmpty(parentId))
                    {
                        // Pattern: USB\VID_xxxx&PID_xxxx\SerialNumber
                        var parentIdMatch = Regex.Match(parentId, @"USB\\VID_[^\\]+\\([^\\]+)$", RegexOptions.IgnoreCase);
                        if (parentIdMatch.Success)
                        {
                            entry.Serial = parentIdMatch.Groups[1].Value.Trim();
                        }
                    }

                    // Extract DiskId for VGUID
                    entry.VGUID = eventDataElements.Elements(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "DiskId")?.Value ?? "";

                    // Extract BusType
                    var busTypeElement = eventDataElements.Elements(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "BusType");
                    if (busTypeElement != null && int.TryParse(busTypeElement.Value, out int busTypeValue))
                    {
                        entry.BusType = MapBusType(busTypeValue);
                    }
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Parse Microsoft-Windows-StorageVolume/Operational log for Event ID 1001 and 1002
        /// </summary>
        private static List<UsbDeviceEntry> ParseStorageVolumeOperationalEvents()
        {
            var entries = new List<UsbDeviceEntry>();
            string logName = "Microsoft-Windows-StorageVolume/Operational";

            try
            {
                string query = "*[System[(EventID=1001 or EventID=1002)]]";
                var eventQuery = new EventLogQuery(logName, PathType.LogName, query);

                using (var eventReader = new EventLogReader(eventQuery))
                {
                    EventRecord eventRecord;

                    while ((eventRecord = eventReader.ReadEvent()) != null)
                    {
                        using (eventRecord)
                        {
                            try
                            {
                                var entry = ParseStorageVolumeOperationalEvent(eventRecord);
                                if (entry != null)
                                {
                                    entries.Add(entry);
                                }
                            }
                            catch
                            {
                                // Skip problematic entries
                            }
                        }
                    }
                }
            }
            catch
            {
                // Log not found or access denied
            }

            return entries;
        }

        /// <summary>
        /// Parse a single StorageVolume Operational event
        /// </summary>
        private static UsbDeviceEntry ParseStorageVolumeOperationalEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                // Extract DiskInstancePath
                var diskInstancePath = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "DiskInstancePath")?.Value ?? "";

                // Only process USBSTOR devices
                if (string.IsNullOrEmpty(diskInstancePath) || 
                    diskInstancePath.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return null;
                }

                var entry = new UsbDeviceEntry
                {
                    Log = "Evtx"
                };

                // Extract timestamp
                var timeCreatedElement = xdoc.Descendants(ns + "TimeCreated").FirstOrDefault();
                if (timeCreatedElement != null)
                {
                    var systemTimeAttr = timeCreatedElement.Attribute("SystemTime");
                    if (systemTimeAttr != null)
                    {
                        if (DateTime.TryParse(systemTimeAttr.Value, null, DateTimeStyles.RoundtripKind, out DateTime parsedTime))
                        {
                            entry.Timestamp = parsedTime.ToLocalTime();
                        }
                    }
                }

                // Get Event ID for action mapping
                var eventIdElement = xdoc.Descendants(ns + "EventID").FirstOrDefault();
                int eventId = eventIdElement != null ? int.Parse(eventIdElement.Value) : 0;

                // Map Event ID to Action
                entry.Action = MapStorageVolumeEventIdToAction(eventId);

                // Parse DiskInstancePath to extract vendor, product, serial
                // Example: USBSTOR\Disk&Ven_Intenso&Prod_Speed_Line&Rev_3.00\24080593020024&0
                ParseDiskInstancePath(diskInstancePath, out string vendor, out string product, out string serial);

                entry.DeviceName = $"{vendor} {product}".Trim();
                entry.Serial = serial;

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Parse a single Kernel-PnP Device Configuration event
        /// </summary>
        private static UsbDeviceEntry ParseKernelPnpDeviceConfigEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                // Extract DeviceInstanceId
                var deviceInstanceId = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "DeviceInstanceId")?.Value ?? "";

                // Only process USB storage devices
                if (!IsUsbStorageDevice(deviceInstanceId))
                {
                    return null;
                }

                var entry = new UsbDeviceEntry
                {
                    Log = "Evtx"
                };

                // Extract timestamp
                var timeCreatedElement = xdoc.Descendants(ns + "TimeCreated").FirstOrDefault();
                if (timeCreatedElement != null)
                {
                    var systemTimeAttr = timeCreatedElement.Attribute("SystemTime");
                    if (systemTimeAttr != null)
                    {
                        if (DateTime.TryParse(systemTimeAttr.Value, null, DateTimeStyles.RoundtripKind, out DateTime parsedTime))
                        {
                            entry.Timestamp = parsedTime.ToLocalTime();
                        }
                    }
                }

                // Get Event ID for action mapping
                var eventIdElement = xdoc.Descendants(ns + "EventID").FirstOrDefault();
                int eventId = eventIdElement != null ? int.Parse(eventIdElement.Value) : 0;

                // Map Event ID to Action
                entry.Action = MapDeviceConfigEventIdToAction(eventId);

                // Parse DeviceInstanceId to extract vendor, product, serial, and VGUID
                ParseDeviceInstanceId(deviceInstanceId, out string vendor, out string product, out string serial, out string vguid);

                entry.DeviceName = $"{vendor} {product}".Trim();
                entry.Serial = serial;
                entry.VGUID = vguid;

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Check if DeviceInstanceId represents a USB storage device
        /// </summary>
        private static bool IsUsbStorageDevice(string deviceInstanceId)
        {
            if (string.IsNullOrEmpty(deviceInstanceId))
                return false;

            // Check for USBSTOR or other USB storage identifiers
            return deviceInstanceId.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   deviceInstanceId.IndexOf("USB\\VID_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Parse DeviceInstanceId to extract vendor, product, serial number, and VGUID
        /// Example: STORAGE\VOLUME\_??_USBSTOR#DISK&VEN_SANDISK&PROD_CRUZER_BLADE&REV_1.00#02003731060121134248&0#{53F56307-B6BF-11D0-94F2-00A0C91EFB8B}
        /// </summary>
        private static void ParseDeviceInstanceId(string deviceInstanceId, out string vendor, out string product, out string serial, out string vguid)
        {
            vendor = "";
            product = "";
            serial = "";
            vguid = "";

            if (string.IsNullOrEmpty(deviceInstanceId))
                return;

            try
            {
                // Extract VGUID (GUID in curly braces)
                var vguidMatch = Regex.Match(deviceInstanceId, @"\{([0-9A-Fa-f\-]+)\}");
                if (vguidMatch.Success)
                {
                    vguid = vguidMatch.Groups[1].Value.ToLowerInvariant();
                }

                // Extract Vendor (VEN_)
                var vendorMatch = Regex.Match(deviceInstanceId, @"VEN_([^&]+)", RegexOptions.IgnoreCase);
                if (vendorMatch.Success)
                {
                    vendor = vendorMatch.Groups[1].Value.Replace("_", " ").Trim();
                }

                // Extract Product (PROD_)
                var productMatch = Regex.Match(deviceInstanceId, @"PROD_([^&]+)", RegexOptions.IgnoreCase);
                if (productMatch.Success)
                {
                    product = productMatch.Groups[1].Value.Replace("_", " ").Trim();
                }

                // Extract Serial Number (between # and &0#)
                var serialMatch = Regex.Match(deviceInstanceId, @"#([^#]+)&0#");
                if (serialMatch.Success)
                {
                    serial = serialMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Alternative pattern: between last # and {
                    var altSerialMatch = Regex.Match(deviceInstanceId, @"#([^#\{]+)(?:#|\{|$)");
                    if (altSerialMatch.Success)
                    {
                        var potentialSerial = altSerialMatch.Groups[1].Value.Trim();
                        // Exclude patterns like &0, REV_, etc.
                        if (!potentialSerial.StartsWith("&") && potentialSerial.IndexOf("REV_", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            serial = potentialSerial;
                        }
                    }
                }
            }
            catch
            {
                // Failed to parse, leave fields empty
            }
        }

        /// <summary>
        /// Map Device Configuration Event ID to Action string
        /// </summary>
        private static string MapDeviceConfigEventIdToAction(int eventId)
        {
            switch (eventId)
            {
                case 400:
                    return "Plugged in";
                case 410:
                    return "Device started";
                case 420:
                    return "Device removed";
                case 430:
                    return "Req. Setup";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Map BusType integer to string representation
        /// </summary>
        private static string MapBusType(int busType)
        {
            switch (busType)
            {
                case 0: return "UNKNOWN";
                case 1: return "SCSI";
                case 2: return "ATAPI";
                case 3: return "ATA";
                case 4: return "1394";
                case 5: return "SSA";
                case 6: return "FIBRE";
                case 7: return "USB";
                case 8: return "RAID";
                case 9: return "ISCSI";
                case 10: return "SAS";
                case 11: return "SATA";
                case 12: return "SD";
                case 13: return "MMC";
                case 14: return "VIRTUAL";
                case 15: return "FILEBACKEDVIRTUAL";
                case 16: return "SPACES";
                case 17: return "NVME";
                case 18: return "SCM";
                case 19: return "UFS";
                case 20: return "MAX";
                default: return "UNKNOWN";
            }
        }

        /// <summary>
        /// Map StorageVolume Event ID to Action string
        /// </summary>
        private static string MapStorageVolumeEventIdToAction(int eventId)
        {
            switch (eventId)
            {
                case 1001:
                    return "Device Connected";
                case 1002:
                    return "Device Disconnected";
                default:
                    return "Device Conig Change";
            }
        }

        /// <summary>
        /// Parse DiskInstancePath to extract vendor, product, and serial number
        /// Example: USBSTOR\Disk&Ven_Intenso&Prod_Speed_Line&Rev_3.00\24080593020024&0
        /// </summary>
        private static void ParseDiskInstancePath(string diskInstancePath, out string vendor, out string product, out string serial)
        {
            vendor = "";
            product = "";
            serial = "";

            if (string.IsNullOrEmpty(diskInstancePath))
                return;

            try
            {
                // Extract Vendor (Ven_)
                var vendorMatch = Regex.Match(diskInstancePath, @"Ven_([^&]+)", RegexOptions.IgnoreCase);
                if (vendorMatch.Success)
                {
                    vendor = vendorMatch.Groups[1].Value.Replace("_", " ").Trim();
                }

                // Extract Product (Prod_)
                var productMatch = Regex.Match(diskInstancePath, @"Prod_([^&]+)", RegexOptions.IgnoreCase);
                if (productMatch.Success)
                {
                    product = productMatch.Groups[1].Value.Replace("_", " ").Trim();
                }

                // Extract Serial Number (between last \ and &)
                var serialMatch = Regex.Match(diskInstancePath, @"\\([^\\]+)&\d+$");
                if (serialMatch.Success)
                {
                    serial = serialMatch.Groups[1].Value.Trim();
                }
            }
            catch
            {
                // Failed to parse, leave fields empty
            }
        }
    }
}
