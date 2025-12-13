using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Registry.Abstractions;
using Timeline.Core.Models;
using Timeline.Core.Util;

// Parse BAM entries to extract execution timestamps
namespace Timeline.Core.Parsers
{
    // Parse Background Activity Moderator registry keys
    public static class BAMParser
    {
        private static readonly SemaphoreSlim BamSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
        private const int BatchSize = 25;
        private const int MaxValueDataSize = 1024;
        private const int MaxSubKeyCount = 1000;

        /// <summary>
        /// Async version of ParseBAMKey with progress reporting, batching, and validation.
        /// </summary>
        public static async Task<List<RegistryEntry>> ParseBAMKeyAsync(RegistryKey bamUserSettingsKey, IProgress<int> progress, CancellationToken cancellationToken)
        {
            await BamSemaphore.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                    var entries = new List<RegistryEntry>(100);
                    if (bamUserSettingsKey == null)
                    {
                        return entries;
                    }

                    var sidKeys = bamUserSettingsKey.SubKeys?.ToList() ?? new List<RegistryKey>();
                    
                    // Limit the number of subkeys to process to prevent excessive memory usage
                    if (sidKeys.Count > MaxSubKeyCount)
                    {
                        sidKeys = sidKeys.Take(MaxSubKeyCount).ToList();
                    }

                    int totalWork = sidKeys.Sum(sk => sk.Values?.Count ?? 0);
                    int processedWork = 0;

                    // The BAM key contains subkeys named after user SIDs.
                    for (int i = 0; i < sidKeys.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        var sidKey = sidKeys[i];
                        if (sidKey.Values == null) continue;

                        var values = sidKey.Values.ToList();
                        
                        // Process values in batches
                        for (int j = 0; j < values.Count; j += BatchSize)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            var batch = values.Skip(j).Take(BatchSize);
                            foreach (var value in batch)
                            {
                                try
                                {
                                    var entry = ProcessBamValue(value, sidKey.KeyName);
                                    if (entry != null)
                                    {
                                        entries.Add(entry);
                                    }
                                }
                                catch (Exception)
                                {
                                }
                                
                                processedWork++;
                            }

                            // Report progress every batch
                            if (totalWork > 0)
                            {
                                progress?.Report((processedWork * 100) / totalWork);
                            }
                        }
                    }
                    
                    progress?.Report(100);
                    return entries;
                }, cancellationToken);
            }
            finally
            {
                BamSemaphore.Release();
            }
        }

        private static RegistryEntry ProcessBamValue(KeyValue value, string sidKeyName)
        {
            try
            {
                // Skip version entries and empty values
                if (string.IsNullOrEmpty(value.ValueName) || 
                    value.ValueName.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
                    return null;

                var data = value.ValueDataRaw;
                
                // Validate data size and content
                if (data == null || data.Length < 8 || data.Length > MaxValueDataSize)
                    return null;

                // The 64-bit FILETIME timestamp is at the beginning of the data.
                long fileTime = BitConverter.ToInt64(data, 0);
                
                // A filetime of 0 is not a valid timestamp.
                if (fileTime == 0) 
                    return null;

                var timestamp = DateTimeOffset.FromFileTime(fileTime);

                // Validate timestamp is reasonable (not too far in past/future)
                var now = DateTimeOffset.Now;
                if (timestamp > now.AddYears(1) || timestamp < now.AddYears(-50))
                    return null;

                // The path is the name of the value.
                string originalPath = value.ValueName;
                
                // Validate path length
                if (originalPath.Length > 1000) // Reasonable path length limit
                    return null;

                // Translate the NT device path to a standard drive letter path.
                string translatedPath = DevicePathMapper.TranslateDevicePath(originalPath);

                return new RegistryEntry
                {
                    Timestamp = timestamp,
                    Source = StringPool.InternSource("BAM"),
                    Description = StringPool.InternDescription("Run Executable"),
                    Path = StringPool.InternPath(translatedPath ?? originalPath),
                    OtherInfo = StringPool.InternOtherInfo($"User SID: {sidKeyName ?? "Unknown"}")
                };
            }
            catch (Exception)
            {
                // Return null on any error to continue processing
                return null;
            }
        }

        public static List<RegistryEntry> ParseBAMKey(RegistryKey bamUserSettingsKey)
        {
            return ParseBAMKeyAsync(bamUserSettingsKey, null, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}