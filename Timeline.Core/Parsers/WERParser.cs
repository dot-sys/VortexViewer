using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Xml.Linq;
using Timeline.Core.Models;
using Timeline.Core.Util;
using System.Collections.Concurrent;

// Parser for Windows Error Reporting
namespace Timeline.Core.Parsers
{
    // Parsed crash entry record
    internal class WEREntry
    {
        public DateTime Timestamp { get; set; }
        public string CrashedProcessPath { get; set; }
        public string ReportFile { get; set; }
    }

    // Extract reports from ProgramData
    public static class WERParser
    {
        // Main parsing entry point
        public static List<RegistryEntry> ParseWERReports(Action<string> logger = null)
        {
            var timelineEntries = new List<RegistryEntry>();

            try
            {
                string systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
                
                if (string.IsNullOrEmpty(systemDrive))
                {
                    systemDrive = "C:";
                }

                if (!systemDrive.EndsWith("\\"))
                {
                    if (!systemDrive.EndsWith(":"))
                    {
                        systemDrive += ":";
                    }
                    systemDrive += "\\";
                }

                string werBasePath = Path.Combine(systemDrive, "ProgramData", "Microsoft", "Windows", "WER");

                if (!Directory.Exists(werBasePath))
                {
                    return timelineEntries;
                }

                var reportQueue = Path.Combine(werBasePath, "ReportQueue");
                var reportArchive = Path.Combine(werBasePath, "ReportArchive");

                var bag = new ConcurrentBag<RegistryEntry>();

                if (Directory.Exists(reportQueue))
                {
                    var queueEntries = ParseWERDirectory(reportQueue);
                    foreach (var e in queueEntries) bag.Add(e);
                }

                if (Directory.Exists(reportArchive))
                {
                    var archiveEntries = ParseWERDirectory(reportArchive);
                    foreach (var e in archiveEntries) bag.Add(e);
                }

                timelineEntries.AddRange(bag);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (Exception)
            {
            }

            return timelineEntries;
        }

        // Parse all reports in directory
        private static List<RegistryEntry> ParseWERDirectory(string directoryPath)
        {
            var entries = new List<RegistryEntry>();

            try
            {
                var reportDirs = Directory.GetDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly);

                if (reportDirs.Length == 0)
                {
                    return entries;
                }

                for (int i = 0; i < reportDirs.Length; i++)
                {
                    var reportDir = reportDirs[i];
                    
                    try
                    {
                        var werEntry = ParseWERReportDirectory(reportDir);
                        
                        if (werEntry != null)
                        {
                            var timelineEntry = ConvertToTimelineEntry(werEntry);
                            
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
            catch (UnauthorizedAccessException)
            {
            }
            catch (Exception)
            {
            }

            return entries;
        }

        // Parse single report folder
        private static WEREntry ParseWERReportDirectory(string reportDir)
        {
            try
            {
                var werFile = Path.Combine(reportDir, "Report.wer");
                if (File.Exists(werFile))
                {
                    return ParseReportWerFile(werFile, reportDir);
                }

                var xmlFiles = Directory.GetFiles(reportDir, "*.xml", SearchOption.TopDirectoryOnly);
                
                if (xmlFiles.Length > 0)
                {
                    return ParseReportXmlFile(xmlFiles[0], reportDir);
                }

                var dirInfo = new DirectoryInfo(reportDir);
                var entry = new WEREntry
                {
                    Timestamp = dirInfo.CreationTime,
                    CrashedProcessPath = "Unknown",
                    ReportFile = reportDir
                };
                
                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Parse text report format
        private static WEREntry ParseReportWerFile(string werFilePath, string reportDir)
        {
            try
            {
                var lines = File.ReadAllLines(werFilePath);
                
                var entry = new WEREntry
                {
                    ReportFile = reportDir
                };

                DateTime? eventTime = null;
                string appPath = null;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (trimmedLine.StartsWith("EventTime=", StringComparison.OrdinalIgnoreCase))
                    {
                        var timeStr = trimmedLine.Substring("EventTime=".Length);
                        
                        if (DateTime.TryParse(timeStr, out DateTime parsedTime))
                        {
                            eventTime = parsedTime;
                        }
                        else if (long.TryParse(timeStr, out long fileTime))
                        {
                            try
                            {
                                eventTime = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                            }
                            catch
                            {
                            }
                        }
                    }

                    if (trimmedLine.StartsWith("AppPath=", StringComparison.OrdinalIgnoreCase))
                    {
                        appPath = trimmedLine.Substring("AppPath=".Length);
                    }
                    else if (trimmedLine.StartsWith("TargetAppPath=", StringComparison.OrdinalIgnoreCase))
                    {
                        appPath = trimmedLine.Substring("TargetAppPath=".Length);
                    }
                    else if (trimmedLine.StartsWith("ApplicationPath=", StringComparison.OrdinalIgnoreCase))
                    {
                        appPath = trimmedLine.Substring("ApplicationPath=".Length);
                    }
                }

                if (eventTime.HasValue)
                {
                    entry.Timestamp = eventTime.Value;
                }
                else
                {
                    entry.Timestamp = File.GetCreationTime(werFilePath);
                }
                
                entry.CrashedProcessPath = appPath ?? "Unknown";

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Parse XML report format
        private static WEREntry ParseReportXmlFile(string xmlFilePath, string reportDir)
        {
            try
            {
                var xdoc = XDocument.Load(xmlFilePath);
                var entry = new WEREntry
                {
                    ReportFile = reportDir
                };

                var timeElement = xdoc.Root?.Element("EventTime") ?? xdoc.Root?.Element("Time");
                if (timeElement != null)
                {
                    if (DateTime.TryParse(timeElement.Value, out DateTime parsedTime))
                    {
                        entry.Timestamp = parsedTime;
                    }
                    else
                    {
                        entry.Timestamp = File.GetCreationTime(xmlFilePath);
                    }
                }
                else
                {
                    entry.Timestamp = File.GetCreationTime(xmlFilePath);
                }

                var appPathElement = xdoc.Root?.Element("AppPath") ?? 
                                    xdoc.Root?.Element("ApplicationPath") ?? 
                                    xdoc.Root?.Element("TargetAppPath");
                
                if (appPathElement != null)
                {
                    entry.CrashedProcessPath = appPathElement.Value;
                }
                else
                {
                    entry.CrashedProcessPath = "Unknown";
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Convert to shared model
        private static RegistryEntry ConvertToTimelineEntry(WEREntry werEntry)
        {
            if (werEntry == null)
                return null;

            // WER stores local time
            var timelineEntry = new RegistryEntry
            {
                Timestamp = new DateTimeOffset(werEntry.Timestamp, TimeZoneInfo.Local.GetUtcOffset(werEntry.Timestamp)),
                Path = StringPool.InternPath(werEntry.CrashedProcessPath),
                Description = StringPool.InternDescription("Process Crash"),
                Source = StringPool.InternSource("WER"),
                OtherInfo = ""
            };

            return timelineEntry;
        }
    }
}
