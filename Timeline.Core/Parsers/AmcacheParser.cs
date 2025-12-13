using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Registry;
using Registry.Abstractions;
using Timeline.Core.Models;
using Timeline.Core.Util;
using Amcache;
using System.Collections.Concurrent;

// Parses Amcache.hve for execution evidence
namespace Timeline.Core.Parsers
{
    // Extracts file execution records from Amcache
    public static class AmcacheParser
    {
        private static readonly SemaphoreSlim AmcacheSemaphore = new SemaphoreSlim(1, 1);
        private const int BatchSize = 100;
        private const long MaxFileSizeBytes = 500 * 1024 * 1024;

        public static async Task<List<RegistryEntry>> ParseAmcacheFileAsync(string amcachePath, IProgress<int> progress, CancellationToken cancellationToken)
        {
            await AmcacheSemaphore.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                    var entries = new List<RegistryEntry>(1000);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!File.Exists(amcachePath))
                        {
                            return entries;
                        }

                        var fileInfo = new FileInfo(amcachePath);
                        if (fileInfo.Length > MaxFileSizeBytes)
                        {
                            return entries;
                        }

                        if (!Helper.IsNewFormat(amcachePath, true))
                        {
                            return entries;
                        }

                        progress?.Report(25);
                        cancellationToken.ThrowIfCancellationRequested();

                        var amcache = new AmcacheNew(amcachePath, true, true);
                        progress?.Report(50);

                        cancellationToken.ThrowIfCancellationRequested();

                        if (amcache.UnassociatedFileEntries != null)
                        {
                            var fileEntries = amcache.UnassociatedFileEntries;
                            int totalEntries = fileEntries.Count;

                            var bag = new ConcurrentBag<RegistryEntry>();

                            int degree = Math.Max(1, Environment.ProcessorCount - 1);

                            Parallel.For(0, totalEntries, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = degree }, idx =>
                            {
                                try
                                {
                                    var fileEntry = fileEntries[idx];
                                    var entry = CreateRegistryEntryFromFileEntry(fileEntry);
                                    if (entry != null)
                                    {
                                        bag.Add(entry);
                                    }
                                }
                                catch
                                {
                                }
                            });

                            entries.AddRange(bag);
                        }
                        
                        
                        progress?.Report(100);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                    }

                    return entries;
                }, cancellationToken);
            }
            finally
            {
                AmcacheSemaphore.Release();
            }
        }

        public static List<RegistryEntry> ParseAmcache(RegistryHive hive)
        {
            if (string.IsNullOrEmpty(hive.HivePath) || !File.Exists(hive.HivePath))
            {
                return new List<RegistryEntry>();
            }
            return ParseAmcacheFile(hive.HivePath);
        }

        public static List<RegistryEntry> ParseAmcacheFile(string amcachePath)
        {
            return ParseAmcacheFileAsync(amcachePath, null, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static RegistryEntry CreateRegistryEntryFromFileEntry(Amcache.Classes.FileEntryNew fileEntry)
        {
            try
            {
                if (fileEntry == null || string.IsNullOrEmpty(fileEntry.FullPath))
                    return null;

                var otherInfo = new StringBuilder();
                
                if (!string.IsNullOrEmpty(fileEntry.SHA1))
                {
                    otherInfo.Append($"SHA1: {fileEntry.SHA1}");
                }

                if (!string.IsNullOrEmpty(fileEntry.ApplicationName))
                {
                    if (otherInfo.Length > 0) otherInfo.Append(", ");
                    otherInfo.Append($"Program: {fileEntry.ApplicationName}");
                }
                if (!string.IsNullOrEmpty(fileEntry.ProductName))
                {
                    if (otherInfo.Length > 0) otherInfo.Append(", ");
                    otherInfo.Append($"Product: {fileEntry.ProductName}");
                }
                if (!string.IsNullOrEmpty(fileEntry.Publisher))
                {
                    if (otherInfo.Length > 0) otherInfo.Append(", ");
                    otherInfo.Append($"Publisher: {fileEntry.Publisher}");
                }
                if (!string.IsNullOrEmpty(fileEntry.Description))
                {
                    if (otherInfo.Length > 0) otherInfo.Append(", ");
                    otherInfo.Append($"Description: {fileEntry.Description}");
                }

                return new RegistryEntry
                {
                    Timestamp = fileEntry.FileKeyLastWriteTimestamp.ToLocalTime(),
                    Source = StringPool.InternSource("Amcache"),
                    Description = StringPool.InternDescription("File Execution"),
                    Path = StringPool.InternPath(fileEntry.FullPath),
                    OtherInfo = otherInfo.ToString()
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}