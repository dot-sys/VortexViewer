using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Timeline.Core.Models;
using Timeline.Core.Util;
using System.Collections.Concurrent;

// Windows event log parser for timeline
namespace Timeline.Core.Parsers
{
    // Single event log entry container
    internal class EventLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Path { get; set; }
        public string Description { get; set; }
        public string Source { get; set; }
        public string OtherInfo { get; set; }
    }

    // Extracts timeline events from event logs
    public static class EventLogParser
    {
        private static readonly HashSet<string> FileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jar", ".msi",
            ".scr", ".com", ".pif", ".cpl", ".hta", ".wsf", ".reg", ".inf", ".bin", ".dat"
        };

        public static List<RegistryEntry> ParseEventLogs(Action<string> logger = null)
        {
            var timelineEntries = new List<RegistryEntry>();

            try
            {
                var allEntries = new ConcurrentBag<RegistryEntry>();

                var defenderEntries = ParseWindowsDefenderEvents();
                foreach (var e in defenderEntries) allEntries.Add(e);

                var appCrashEntries = ParseApplicationCrashEvents();
                foreach (var e in appCrashEntries) allEntries.Add(e);

                var serviceStartedEntries = ParseSystemServiceStartedEvents();
                foreach (var e in serviceStartedEntries) allEntries.Add(e);

                var processCreatedEntries = ParseSecurityProcessCreatedEvents();
                foreach (var e in processCreatedEntries) allEntries.Add(e);

                var userLogonEntries = ParseSecurityUserLogonEvents();
                foreach (var e in userLogonEntries) allEntries.Add(e);

                var powershellEntries = ParsePowerShellEvents();
                foreach (var e in powershellEntries) allEntries.Add(e);

                var defenderConfigEntries = ParseWindowsDefenderConfigEvents();
                foreach (var e in defenderConfigEntries) allEntries.Add(e);

                timelineEntries.AddRange(allEntries);
            }
            catch (Exception)
            {
            }

            return timelineEntries;
        }

        private static string ExtractCleanPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return "Unknown";

            var drivePattern = new Regex(@"[A-Za-z]:\\");
            var match = drivePattern.Match(rawPath);

            if (!match.Success)
                return rawPath;

            int startIndex = match.Index;
            string pathFromDrive = rawPath.Substring(startIndex);

            int bestEndIndex = pathFromDrive.Length;

            foreach (var extension in FileExtensions)
            {
                int extIndex = pathFromDrive.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
                if (extIndex >= 0)
                {
                    int endIndex = extIndex + extension.Length;
                    if (endIndex < bestEndIndex)
                    {
                        bestEndIndex = endIndex;
                    }
                }
            }

            string cleanPath = pathFromDrive.Substring(0, bestEndIndex);

            if (cleanPath.Contains(":_"))
            {
                int prefixEnd = cleanPath.IndexOf(":_") + 2;
                if (prefixEnd < cleanPath.Length)
                {
                    cleanPath = cleanPath.Substring(prefixEnd);
                }
            }

            return cleanPath.Trim();
        }

        private static List<RegistryEntry> ParseWindowsDefenderEvents()
        {
            var entries = new List<RegistryEntry>();
            string logName = @"Microsoft-Windows-Windows Defender/Operational";

            try
            {
                string query = "*[System[(EventID=1115 or EventID=1116)]]";
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
                                var eventLogEntry = ParseDefenderEvent(eventRecord);

                                if (eventLogEntry != null)
                                {
                                    var timelineEntry = ConvertToTimelineEntry(eventLogEntry);
                                    if (timelineEntry != null)
                                    {
                                        entries.Add(timelineEntry);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static List<RegistryEntry> ParseApplicationCrashEvents()
        {
            var entries = new List<RegistryEntry>();
            string logName = "Application";

            try
            {
                string query = "*[System[(EventID=1000)]]";
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
                                var eventLogEntry = ParseApplicationCrashEvent(eventRecord);

                                if (eventLogEntry != null)
                                {
                                    var timelineEntry = ConvertToTimelineEntry(eventLogEntry);
                                    if (timelineEntry != null)
                                    {
                                        entries.Add(timelineEntry);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static List<RegistryEntry> ParseSystemServiceStartedEvents()
        {
            var entries = new List<RegistryEntry>();
            string logName = "System";

            try
            {
                string query = "*[System[(EventID=7045)]]";
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
                                var eventLogEntry = ParseSystemServiceStartedEvent(eventRecord);

                                if (eventLogEntry != null)
                                {
                                    var timelineEntry = ConvertToTimelineEntry(eventLogEntry);
                                    if (timelineEntry != null)
                                    {
                                        entries.Add(timelineEntry);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static List<RegistryEntry> ParseSecurityProcessCreatedEvents()
        {
            var entries = new List<RegistryEntry>();
            string logName = "Security";

            try
            {
                string query = "*[System[(EventID=4688)]]";
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
                                var eventLogEntry = ParseSecurityProcessCreatedEvent(eventRecord);

                                if (eventLogEntry != null)
                                {
                                    var timelineEntry = ConvertToTimelineEntry(eventLogEntry);
                                    if (timelineEntry != null)
                                    {
                                        entries.Add(timelineEntry);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static List<RegistryEntry> ParseSecurityUserLogonEvents()
        {
            var entries = new List<RegistryEntry>();
            string logName = "Security";

            try
            {
                string query = "*[System[(EventID=4624)]]";
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
                                var eventLogEntry = ParseSecurityUserLogonEvent(eventRecord);

                                if (eventLogEntry != null)
                                {
                                    var timelineEntry = ConvertToTimelineEntry(eventLogEntry);
                                    if (timelineEntry != null)
                                    {
                                        entries.Add(timelineEntry);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static List<RegistryEntry> ParsePowerShellEvents()
        {
            var entries = new List<RegistryEntry>();
            string logName = @"Microsoft-Windows-PowerShell/Operational";

            try
            {
                string query = "*[System[(EventID=4104)]]";
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
                                var eventLogEntry = ParsePowerShellEvent(eventRecord);

                                if (eventLogEntry != null)
                                {
                                    var timelineEntry = ConvertToTimelineEntry(eventLogEntry);
                                    if (timelineEntry != null)
                                    {
                                        entries.Add(timelineEntry);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static List<RegistryEntry> ParseWindowsDefenderConfigEvents()
        {
            var entries = new List<RegistryEntry>();
            string logName = @"Microsoft-Windows-Windows Defender/Operational";

            try
            {
                string query = "*[System[(EventID=5001 or EventID=5007 or EventID=5013)]]";
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
                                var eventLogEntry = ParseWindowsDefenderConfigEvent(eventRecord);

                                if (eventLogEntry != null)
                                {
                                    var timelineEntry = ConvertToTimelineEntry(eventLogEntry);
                                    if (timelineEntry != null)
                                    {
                                        entries.Add(timelineEntry);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static EventLogEntry ParseDefenderEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new EventLogEntry
                {
                    Description = "Threat",
                    Source = "Eventlog"
                };

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

                var threatNameElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "Threat Name");
                if (threatNameElement != null)
                {
                    entry.OtherInfo = threatNameElement.Value;
                }

                var pathElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "Path");
                if (pathElement != null)
                {
                    string rawPath = pathElement.Value;
                    entry.Path = ExtractCleanPath(rawPath);
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static EventLogEntry ParseApplicationCrashEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new EventLogEntry
                {
                    Description = "Process Crash",
                    Source = "Eventlog",
                    OtherInfo = ""
                };

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

                var appPathElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "AppPath");
                if (appPathElement != null)
                {
                    string rawPath = appPathElement.Value;
                    entry.Path = ExtractCleanPath(rawPath);
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static EventLogEntry ParseSystemServiceStartedEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new EventLogEntry
                {
                    Description = "Service Started",
                    Source = "EventLog"
                };

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

                var imagePathElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "ImagePath");
                if (imagePathElement != null)
                {
                    string imagePath = imagePathElement.Value;
                    entry.Path = WindowsPathExtractor.ExtractPath(imagePath);
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static EventLogEntry ParseSecurityProcessCreatedEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new EventLogEntry
                {
                    Description = "Process Created",
                    Source = "EventLog"
                };

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

                var newProcessNameElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "NewProcessName");
                if (newProcessNameElement != null)
                {
                    entry.Path = newProcessNameElement.Value;
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static EventLogEntry ParseSecurityUserLogonEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var logonProcessNameElement = xdoc.Descendants(ns + "Data")
                    .FirstOrDefault(d => d.Attribute("Name")?.Value == "LogonProcessName");
                
                if (logonProcessNameElement == null || logonProcessNameElement.Value.Trim() != "User32")
                {
                    return null;
                }

                var entry = new EventLogEntry
                {
                    Description = "User Logon",
                    Source = "EventLog",
                    Path = "User Logon"
                };

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

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static EventLogEntry ParsePowerShellEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new EventLogEntry
                {
                    Description = "Powershell",
                    Source = "EventLog",
                    Path = "Powershell Script Block Executed"
                };

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

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static EventLogEntry ParseWindowsDefenderConfigEvent(EventRecord eventRecord)
        {
            try
            {
                string xmlString = eventRecord.ToXml();
                var xdoc = XDocument.Parse(xmlString);
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                var entry = new EventLogEntry
                {
                    Description = "Defender",
                    Source = "EventLog"
                };

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

                var eventIdElement = xdoc.Descendants(ns + "EventID").FirstOrDefault();
                int eventId = eventIdElement != null ? int.Parse(eventIdElement.Value) : 0;

                if (eventId == 5001)
                {
                    entry.Path = "Defender Disabled";
                }
                else if (eventId == 5007)
                {
                    var oldValueElement = xdoc.Descendants(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "Old Value");
                    var newValueElement = xdoc.Descendants(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "New Value");

                    string oldValue = oldValueElement?.Value?.Trim() ?? "";
                    string newValue = newValueElement?.Value?.Trim() ?? "";

                    if (!oldValue.Contains("Exclusions") && !newValue.Contains("Exclusions"))
                    {
                        return null;
                    }

                    if (!string.IsNullOrEmpty(newValue) && string.IsNullOrEmpty(oldValue))
                    {
                        string extractedPath = ExtractDefenderExclusionPath(newValue);
                        entry.Path = $"Defender Exclusion Added: {extractedPath}";
                    }
                    else if (!string.IsNullOrEmpty(oldValue) && string.IsNullOrEmpty(newValue))
                    {
                        string extractedPath = ExtractDefenderExclusionPath(oldValue);
                        entry.Path = $"Defender Exclusion Removed: {extractedPath}";
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (eventId == 5013)
                {
                    var valueElement = xdoc.Descendants(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "Value");
                    var changedTypeElement = xdoc.Descendants(ns + "Data")
                        .FirstOrDefault(d => d.Attribute("Name")?.Value == "Changed Type");

                    string value = valueElement?.Value?.Trim() ?? "";
                    string changedType = changedTypeElement?.Value?.Trim() ?? "";

                    if (value.Contains("DisableRealtimeMonitoring") && changedType.Equals("Reverted", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Path = "Defender Enabled";
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ExtractDefenderExclusionPath(string registryValue)
        {
            if (string.IsNullOrEmpty(registryValue))
                return "Unknown";

            int pathsIndex = registryValue.IndexOf(@"\Paths\", StringComparison.OrdinalIgnoreCase);
            if (pathsIndex >= 0)
            {
                var afterPaths = registryValue.Substring(pathsIndex + 7);
                int equalsIndex = afterPaths.IndexOf(" = ");
                if (equalsIndex >= 0)
                {
                    return afterPaths.Substring(0, equalsIndex).Trim();
                }
                return afterPaths.Trim();
            }

            int extensionsIndex = registryValue.IndexOf(@"\Extensions\", StringComparison.OrdinalIgnoreCase);
            if (extensionsIndex >= 0)
            {
                var afterExtensions = registryValue.Substring(extensionsIndex + 12);
                int equalsIndex = afterExtensions.IndexOf(" = ");
                if (equalsIndex >= 0)
                {
                    return afterExtensions.Substring(0, equalsIndex).Trim();
                }
                return afterExtensions.Trim();
            }

            return registryValue;
        }

        private static RegistryEntry ConvertToTimelineEntry(EventLogEntry eventLogEntry)
        {
            if (eventLogEntry == null)
                return null;

            var timelineEntry = new RegistryEntry
            {
                Timestamp = new DateTimeOffset(eventLogEntry.Timestamp, TimeZoneInfo.Local.GetUtcOffset(eventLogEntry.Timestamp)),
                Path = StringPool.InternPath(eventLogEntry.Path ?? "Unknown"),
                Description = StringPool.InternDescription(eventLogEntry.Description),
                Source = StringPool.InternSource(eventLogEntry.Source),
                OtherInfo = StringPool.InternOtherInfo(eventLogEntry.OtherInfo ?? "")
            };

            return timelineEntry;
        }
    }
}
