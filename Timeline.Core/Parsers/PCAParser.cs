using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Timeline.Core.Models;
using Timeline.Core.Util;

// Windows PCA compatibility database extraction utilities
namespace Timeline.Core.Parsers
{
    // Single PCA database entry record
    internal class PCAEntry
    {
        public DateTime Timestamp { get; set; }
        public int EventTypeCode { get; set; }
        public string FilePath { get; set; }
        public string ProcessName { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string Hash { get; set; }
        public string EventDescription { get; set; }

        public bool IsUnusual()
        {
            return string.IsNullOrWhiteSpace(ProcessName) && 
                   string.IsNullOrWhiteSpace(Publisher) && 
                   string.IsNullOrWhiteSpace(Version);
        }
    }

    // App launch dictionary entry record
    internal class PCAAppLaunchEntry
    {
        public DateTime Timestamp { get; set; }
        public string FilePath { get; set; }
    }

    // Parses Windows PCA compatibility database files
    public static class PCAParser
    {
        // Main entry point for PCA parsing
        public static List<RegistryEntry> ParsePCADatabase(Action<string> logger = null)
        {
            var timelineEntries = new List<RegistryEntry>();

            try
            {
                string systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
                if (string.IsNullOrEmpty(systemDrive))
                    systemDrive = "C:";
                
                if (!systemDrive.EndsWith(":"))
                    systemDrive += ":";


                string pcaFolderPath = systemDrive + @"\Windows\appcompat\pca";
                string pcaGeneralDb0Path = Path.Combine(pcaFolderPath, "PcaGeneralDb0.txt");
                string pcaGeneralDb1Path = Path.Combine(pcaFolderPath, "PcaGeneralDb1.txt");
                string pcaAppLaunchDicPath = Path.Combine(pcaFolderPath, "PcaAppLaunchDic.txt");

                var appLaunchLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                
                if (File.Exists(pcaAppLaunchDicPath))
                {
                    var appLaunchEntries = ParsePCAAppLaunchDic(pcaAppLaunchDicPath);
                    
                    foreach (var entry in appLaunchEntries)
                    {
                        var pathKey = GetPathWithoutDrive(entry.FilePath);
                        if (!string.IsNullOrEmpty(pathKey))
                        {
                            appLaunchLookup[pathKey] = entry.FilePath;
                        }
                        
                        timelineEntries.Add(new RegistryEntry
                        {
                            Timestamp = new DateTimeOffset(entry.Timestamp, TimeZoneInfo.Local.GetUtcOffset(entry.Timestamp)),
                            Path = StringPool.InternPath(entry.FilePath),
                            Description = StringPool.InternDescription("Run Executable"),
                            Source = StringPool.InternSource("PCA"),
                            OtherInfo = ""
                        });
                    }
                }

                if (File.Exists(pcaGeneralDb0Path))
                {
                    ParseAndAddPCAGeneralDbEntries(pcaGeneralDb0Path, appLaunchLookup, timelineEntries);
                }

                if (File.Exists(pcaGeneralDb1Path))
                {
                    ParseAndAddPCAGeneralDbEntries(pcaGeneralDb1Path, appLaunchLookup, timelineEntries);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (Exception)
            {
            }

            return timelineEntries;
        }

        // Parse and convert entries to timeline
        private static void ParseAndAddPCAGeneralDbEntries(string filePath, Dictionary<string, string> appLaunchLookup, List<RegistryEntry> timelineEntries)
        {
            var pcaEntries = ParsePCAFile(filePath);

            int reconstructedCount = 0;

            foreach (var pcaEntry in pcaEntries)
            {
                var timelineEntry = ConvertToTimelineEntry(pcaEntry, appLaunchLookup, ref reconstructedCount);
                if (timelineEntry != null)
                {
                    timelineEntries.Add(timelineEntry);
                }
            }
        }

        // Parse app launch dictionary file
        private static List<PCAAppLaunchEntry> ParsePCAAppLaunchDic(string filePath)
        {
            var entries = new List<PCAAppLaunchEntry>();

            try
            {
                var lines = File.ReadAllLines(filePath, System.Text.Encoding.Unicode);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var parts = line.Split('|');
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    try
                    {
                        var path = parts[0].Trim();
                        var timestampStr = parts[1].Trim();

                        string[] formats = new[] 
                        { 
                            "yyyy-MM-dd HH:mm:ss.fff",
                            "yyyy-MM-dd HH:mm:ss.ff",
                            "yyyy-MM-dd HH:mm:ss.f",
                            "yyyy-MM-dd HH:mm:ss"
                        };
                        
                        if (DateTime.TryParseExact(timestampStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime timestamp))
                        {
                            entries.Add(new PCAAppLaunchEntry
                            {
                                FilePath = path,
                                Timestamp = timestamp
                            });
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

            return entries;
        }

        // Strip drive letter from path
        private static string GetPathWithoutDrive(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                return path.Substring(2);

            if (path.StartsWith("\\"))
            {
                return path;
            }

            return null;
        }

        // Rebuild corrupted path using lookup
        private static string ReconstructPath(string corruptedPath, Dictionary<string, string> appLaunchLookup)
        {
            if (string.IsNullOrEmpty(corruptedPath))
                return corruptedPath;

            if (corruptedPath.Length >= 2 && corruptedPath[1] == ':')
                return corruptedPath;

            var pathKey = corruptedPath.StartsWith("\\") ? corruptedPath : "\\" + corruptedPath;
            
            
            if (appLaunchLookup.TryGetValue(pathKey, out var reconstructedPath))
                return reconstructedPath;

            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.Name.TrimEnd('\\'))
                    .ToList();

                foreach (var drive in drives)
                {
                    var testPath = drive + pathKey;
                    if (File.Exists(testPath))
                        return testPath;
                }
            }
            catch
            {
            }

            return ":" + pathKey;
        }

        // Read and parse PCA file
        private static List<PCAEntry> ParsePCAFile(string filePath)
        {
            var entries = new List<PCAEntry>();

            try
            {
                var lines = File.ReadAllLines(filePath, System.Text.Encoding.Unicode);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var entry = ParsePCALine(line);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            catch (Exception)
            {
            }

            return entries;
        }

        // Parse single pipe-delimited line
        private static PCAEntry ParsePCALine(string line)
        {
            try
            {
                var fields = line.Split('|');
                
                if (fields.Length != 8)
                {
                    return null;
                }

                var entry = new PCAEntry();

                string timestampStr = fields[0].Trim();
                
                string[] formats = new[] 
                { 
                    "yyyy-MM-dd HH:mm:ss.fff",
                    "yyyy-MM-dd HH:mm:ss.ff",
                    "yyyy-MM-dd HH:mm:ss.f",
                    "yyyy-MM-dd HH:mm:ss"
                };
                
                if (!DateTime.TryParseExact(timestampStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime timestamp))
                {
                    return null;
                }
                
                
                entry.Timestamp = timestamp;

                if (int.TryParse(fields[1], out int eventCode))
                    entry.EventTypeCode = eventCode;

                entry.FilePath = fields[2].Trim();
                entry.ProcessName = fields[3].Trim();
                entry.Publisher = fields[4].Trim();
                entry.Version = fields[5].Trim();
                entry.Hash = fields[6].Trim();
                entry.EventDescription = fields[7].Trim();

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Convert PCA entry to timeline
        private static RegistryEntry ConvertToTimelineEntry(PCAEntry pcaEntry, Dictionary<string, string> appLaunchLookup, ref int reconstructedCount)
        {
            if (pcaEntry == null)
                return null;

            var filePath = pcaEntry.FilePath;
            
            if (!string.IsNullOrEmpty(filePath) && filePath.StartsWith("\\") && !(filePath.Length >= 2 && filePath[1] == ':'))
            {
                var originalPath = filePath;
                filePath = ReconstructPath(filePath, appLaunchLookup);
                
                if (filePath != originalPath)
                    reconstructedCount++;
            }

            var timelineEntry = new RegistryEntry
            {
                Timestamp = new DateTimeOffset(pcaEntry.Timestamp, TimeZoneInfo.Local.GetUtcOffset(pcaEntry.Timestamp)),
                Path = StringPool.InternPath(filePath),
                Description = StringPool.InternDescription("Run Executable"),
                Source = StringPool.InternSource("PCA"),
                OtherInfo = pcaEntry.IsUnusual() ? StringPool.InternOtherInfo("Unusual") : ""
            };

            return timelineEntry;
        }
    }
}
