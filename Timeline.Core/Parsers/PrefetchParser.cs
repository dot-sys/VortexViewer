using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Prefetch;
using Timeline.Core.Models;
using Timeline.Core.Util;
using System.Collections.Concurrent;
using System.Threading.Tasks;

// Prefetch parsing utilities for timeline reconstruction
namespace Timeline.Core.Parsers
{
    // Represent essential parsed prefetch data
    internal class PrefetchEntry
    {
        public string ExecutableFilename { get; set; }
        public int RunCount { get; set; }
        public string SourceFilename { get; set; }
        public List<DateTime> LastRunTimes { get; set; }
        public List<string> Filenames { get; set; }
        public string ResolvedPath { get; set; }

        public PrefetchEntry()
        {
            LastRunTimes = new List<DateTime>();
            Filenames = new List<string>();
        }

        public string GetOtherInfo()
        {
            return $"Run Count: {RunCount}";
        }
    }

    // Convert prefetch files into timeline registry entries
    public static class PrefetchParser
    {
        private static readonly Regex VolumeGuidRegex = new Regex(@"^VOLUME\{([0-9a-fA-F\-]+)\}\\(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<RegistryEntry> ParsePrefetchFiles(Action<string> logger = null)
        {
            var timelineEntries = new List<RegistryEntry>();

            try
            {
                string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");

                if (!Directory.Exists(prefetchDir))
                {
                    return timelineEntries;
                }

                var prefetchFiles = Directory.GetFiles(prefetchDir, "*.pf", SearchOption.TopDirectoryOnly);

                if (prefetchFiles.Length == 0)
                {
                    return timelineEntries;
                }

                var bag = new ConcurrentBag<RegistryEntry>();

                Parallel.ForEach(prefetchFiles, filePath =>
                {
                    try
                    {
                        var prefetchEntry = ParseSinglePrefetchFile(filePath);

                        if (prefetchEntry != null)
                        {
                            var entries = ConvertToTimelineEntries(prefetchEntry);
                            foreach (var e in entries)
                                bag.Add(e);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    catch
                    {
                    }
                });

                timelineEntries.AddRange(bag);
            }
            catch (Exception)
            {
            }

            return timelineEntries;
        }

        private static PrefetchEntry ParseSinglePrefetchFile(string filePath)
        {
            IPrefetch pf = PrefetchFile.Open(filePath);

            if (pf == null)
                return null;

            var entry = new PrefetchEntry
            {
                ExecutableFilename = pf.Header?.ExecutableFilename,
                RunCount = pf.RunCount,
                SourceFilename = pf.SourceFilename
            };

            if (pf.LastRunTimes != null && pf.LastRunTimes.Count > 0)
            {
                entry.LastRunTimes = pf.LastRunTimes.Select(dt => dt.UtcDateTime).ToList();
            }

            if (pf.Filenames != null && pf.Filenames.Count > 0)
            {
                entry.Filenames = pf.Filenames.ToList();
            }

            entry.ResolvedPath = ResolveExecutablePath(entry.ExecutableFilename, entry.Filenames);

            return entry;
        }

        private static string ResolveExecutablePath(string executableName, List<string> filenames)
        {
            if (string.IsNullOrEmpty(executableName) || filenames == null || filenames.Count == 0)
                return null;

            var matchedPath = filenames.FirstOrDefault(f =>
                f.EndsWith(executableName, StringComparison.OrdinalIgnoreCase) &&
                f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(matchedPath))
                return null;

            return CleanupAndResolvePath(matchedPath);
        }

        private static string CleanupAndResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string originalPath = path;

            path = path.TrimStart('\\');

            var match = VolumeGuidRegex.Match(path);

            if (match.Success)
            {
                var volumeIdentifier = match.Groups[1].Value;
                var remainingPath = match.Groups[2].Value;

                var driveLetter = VolumeSerialNumberMapper.GetDriveLetterFromPrefetchVolume(volumeIdentifier);

                if (!string.IsNullOrEmpty(driveLetter))
                {
                    return $"{driveLetter}\\{remainingPath}";
                }

                driveLetter = DevicePathMapper.GetDriveLetterFromPartialGuid(volumeIdentifier);

                if (!string.IsNullOrEmpty(driveLetter))
                {
                    return $"{driveLetter}\\{remainingPath}";
                }

                var volumePath = $@"\\?\Volume{{{volumeIdentifier}}}";
                driveLetter = DevicePathMapper.GetDriveLetterFromVolume(volumePath);

                if (!string.IsNullOrEmpty(driveLetter))
                {
                    return $"{driveLetter}\\{remainingPath}";
                }

                try
                {
                    var description = GuidMapping.GuidMapping.GetDescriptionFromGuid(volumeIdentifier);
                    if (!string.IsNullOrEmpty(description))
                    {
                        return $"[{description}]\\{remainingPath}";
                    }
                }
                catch
                {
                }

                return $"[Unmapped Volume: {volumeIdentifier}]\\{remainingPath}";
            }

            return path;
        }

        private static List<RegistryEntry> ConvertToTimelineEntries(PrefetchEntry prefetchEntry)
        {
            var entries = new List<RegistryEntry>();

            if (prefetchEntry.LastRunTimes == null || prefetchEntry.LastRunTimes.Count == 0)
                return entries;

            foreach (var runTime in prefetchEntry.LastRunTimes)
            {
                var localTime = runTime.ToLocalTime();

                var timelineEntry = new RegistryEntry
                {
                    Timestamp = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime)),
                    Path = StringPool.InternPath(prefetchEntry.ResolvedPath ?? prefetchEntry.ExecutableFilename ?? "Unknown"),
                    Description = StringPool.InternDescription("Run Executable"),
                    Source = StringPool.InternSource("Prefetch"),
                    OtherInfo = StringPool.InternOtherInfo(prefetchEntry.GetOtherInfo())
                };

                entries.Add(timelineEntry);
            }

            return entries;
        }
    }
}
