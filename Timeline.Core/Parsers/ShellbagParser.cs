using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Registry;
using Registry.Abstractions;
using Timeline.Core.Models;
using Timeline.Core.Util;

// Parses Windows ShellBags from registry
namespace Timeline.Core.Parsers
{
    // Tracks folder access history from shellbags
    public static class ShellbagParser
    {
        private static readonly SemaphoreSlim ShellbagSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
        private const int MaxRecursionDepth = 10;
        private static int ShellbagCounter = 0;

        public static async Task<List<RegistryEntry>> ParseShellbagsAsync(RegistryHive hive, Action<string> logAction)
        {
            await ShellbagSemaphore.WaitAsync();
            try
            {
                ShellbagCounter = 0;
                var entries = new List<ShellBagMetadata>();
                var registryEntries = new List<RegistryEntry>();

                string bagMRUPath = hive.HiveType == HiveTypeEnum.NtUser
                    ? @"Software\Microsoft\Windows\Shell\BagMRU"
                    : @"Local Settings\Software\Microsoft\Windows\Shell\BagMRU";

                var bagMRUKey = hive.GetKey(bagMRUPath);
                if (bagMRUKey == null)
                {
                    logAction?.Invoke($"BagMRU key not found at {bagMRUPath}");
                    return registryEntries;
                }

                logAction?.Invoke($"Found BagMRU at {bagMRUPath}, starting parse...");
                await Task.Run(() => ParseBagMRURecursive(bagMRUKey, entries, "", 0, logAction));
                logAction?.Invoke($"ConvertToRegistryEntries: metadata.Count={entries.Count}");
                ConvertToRegistryEntries(entries, registryEntries);
                logAction?.Invoke($"ConvertToRegistryEntries: registryEntries.Count={registryEntries.Count}");
                return registryEntries;
            }
            finally
            {
                logAction?.Invoke("[END SHELLBAG]");
                ShellbagSemaphore.Release();
            }
        }

        private static void ConvertToRegistryEntries(List<ShellBagMetadata> metadata, List<RegistryEntry> registryEntries)
        {
            foreach (var item in metadata)
            {
                if (item.OutputPath != null && item.OutputPath.Contains("[Corrupted]"))
                {
                    continue;
                }

                string otherInfo = item.RegIsDeleted ? "Deleted" : "";

                var timestamps = new List<(DateTime? timestamp, string description)>
                {
                    (item.CreatedOnTime, "Created Folder"),
                    (item.LastWriteTime, "Accessed Folder"),
                    (item.LastModificationTime, "Changed Folder")
                };

                var validTimestamps = timestamps
                    .Where(t => t.timestamp.HasValue)
                    .Select(t => (
                        timestamp: t.timestamp.Value,
                        formatted: t.timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                        description: t.description
                    ))
                    .ToList();

                var uniqueTimestamps = new List<(DateTime timestamp, string formatted, string description)>();
                var seenFormattedTimestamps = new HashSet<string>();

                foreach (var ts in validTimestamps)
                {
                    if (!seenFormattedTimestamps.Contains(ts.formatted))
                    {
                        uniqueTimestamps.Add(ts);
                        seenFormattedTimestamps.Add(ts.formatted);
                    }
                }

                foreach (var ts in uniqueTimestamps)
                {
                    registryEntries.Add(new RegistryEntry
                    {
                        Timestamp = new DateTimeOffset(ts.timestamp, TimeSpan.Zero),
                        Description = ts.description,
                        Path = item.OutputPath,
                        OtherInfo = otherInfo,
                        Source = "Shellbag"
                    });
                }
            }
        }

        public static async Task<List<RegistryEntry>> ParseShellbagsAsync(
            RegistryHive hive,
            IProgress<int> _,
            CancellationToken _1,
            Action<string> logAction)
        {
            return await ParseShellbagsAsync(hive, logAction);
        }

        public static List<RegistryEntry> ParseShellbags(RegistryHive hive, Action<string> logAction = null)
        {
            return ParseShellbagsAsync(hive, logAction).GetAwaiter().GetResult();
        }

        private static void ParseBagMRURecursive(RegistryKey mruKey, List<ShellBagMetadata> entries, string parentPath, int depth, Action<string> logAction)
        {
            if (depth > MaxRecursionDepth)
            {
                logAction?.Invoke($"Max recursion depth {MaxRecursionDepth} reached at path: {parentPath}");
                return;
            }
            if (mruKey == null)
            {
                logAction?.Invoke($"MRUKey is null at path: {parentPath}");
                return;
            }
            if (mruKey.Values == null || mruKey.Values.Count == 0)
            {
                logAction?.Invoke($"No values in MRUKey at path: {parentPath}");
                return;
            }

            foreach (var value in mruKey.Values.Where(v => int.TryParse(v.ValueName, out int tmp)))
            {
                logAction?.Invoke($"Processing Value: Name={value.ValueName}, HasRaw={value.ValueDataRaw != null}, RawLen={(value.ValueDataRaw != null ? value.ValueDataRaw.Length : 0)}");
                try
                {
                    byte[] rawBytes = value.ValueDataRaw;
                    if (rawBytes == null || rawBytes.Length < 2)
                    {
                        logAction?.Invoke($"Skipped Value: Name={value.ValueName}, rawBytes null or too short");
                        continue;
                    }
                    ShellbagCounter++;
                    var metadata = ShellBagMetadataExtractor.ExtractMetadata(rawBytes, mruKey, value.ValueName, parentPath, ShellbagCounter);
                    if (metadata == null)
                    {
                        logAction?.Invoke($"Metadata extraction failed for Value: {value.ValueName}");
                        continue;
                    }
                    if (!metadata.IsValid)
                    {
                        logAction?.Invoke($"Metadata invalid for Value: {value.ValueName}");
                        continue;
                    }
                    entries.Add(metadata);
                    logAction?.Invoke($"Metadata added for Value: {value.ValueName}");
                    string subKeyName = value.ValueName;
                    var subKey = mruKey.SubKeys.FirstOrDefault(sk => sk.KeyName == subKeyName);
                    if (subKey != null)
                    {
                        logAction?.Invoke($"Recurse into subKey: {subKeyName}");
                        ParseBagMRURecursive(subKey, entries, metadata.AbsolutePath, depth + 1, logAction);
                    }
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"Exception for Value: {value.ValueName}, ex: {ex}");
                }
            }
        }
    }
}
