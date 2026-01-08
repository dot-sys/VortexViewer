using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AppCompatCache;
using Timeline.Core.Models;
using Timeline.Core.Util;

// Parses Application Compatibility Cache
namespace Timeline.Core.Parsers
{
    // Extracts executed program list from Shimcache
    public static class ShimcacheParser
    {
        public static async Task<List<RegistryEntry>> ParseShimcacheAsync(
            string systemHivePath, 
            Action<string> _ = null, 
            CancellationToken cancellationToken = default)
        {
            var entries = new List<RegistryEntry>(1000);
            
            if (string.IsNullOrEmpty(systemHivePath) || !File.Exists(systemHivePath))
            {
                return entries;
            }

            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    bool is32Bit = AppCompatCache.AppCompatCache.Is32Bit(systemHivePath, null);
                    
                    int controlSet = 1;
                    
                    var appCompatCache = new AppCompatCache.AppCompatCache(systemHivePath, controlSet, noLogs: true);
                    
                    if (appCompatCache.OperatingSystem != AppCompatCache.AppCompatCache.OperatingSystemVersion.Windows10 &&
                        appCompatCache.OperatingSystem != AppCompatCache.AppCompatCache.OperatingSystemVersion.Windows10C_11)
                    {
                        // OS version not in preferred list, processing anyway
                    }
                    
                    if (appCompatCache.Caches == null || appCompatCache.Caches.Count == 0)
                    {
                        return;
                    }
                    
                    foreach (var cache in appCompatCache.Caches)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        foreach (var cacheEntry in cache.Entries)
                        {
                            try
                            {
                                string executionStatus = cacheEntry.Executed.ToString();
                                
                                DateTimeOffset timestamp;
                                if (cacheEntry.LastModifiedTimeUTC.HasValue)
                                {
                                    timestamp = cacheEntry.LastModifiedTimeUTC.Value.ToLocalTime();
                                }
                                else
                                {
                                    timestamp = DateTimeOffset.MinValue;
                                }
                                
                                var registryEntry = new RegistryEntry
                                {
                                    Timestamp = timestamp,
                                    Source = StringPool.InternSource("ShimCache"),
                                    Description = StringPool.InternDescription("Run Executable"),
                                    Path = StringPool.InternPath(cacheEntry.Path ?? "(Unknown Path)"),
                                    OtherInfo = StringPool.InternOtherInfo($"Executed: {executionStatus}")
                                };
                                
                                entries.Add(registryEntry);
                            }
                            catch { }
                        }
                    }
                    
                }, cancellationToken);
                
                return entries;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return entries;
            }
        }
    }
}