using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Registry;
using Registry.Abstractions;
using Timeline.Core.Models;
using Timeline.Core.Util;
using Timeline.Core.Parsers;

// Hive extraction and aggregation
namespace Timeline.Core.Core
{
    // Orchestrates registry data collection
    public static class RegistryExtractor
    {
        // Adaptive parser throttle
        private static readonly SemaphoreSlim ParserSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
        // Maximum concurrent threads
        private static readonly int MaxConcurrentParsers = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

        public static async Task<ExtractionResult> ExtractFromStandardLocationsAsync(IProgress<string> progress, CancellationToken cancellationToken = default, bool uppercaseResults = true)
        {
            IEnumerable<string> hivePaths = new List<string>();
            List<RegistryEntry> finalEntries = new List<RegistryEntry>();

            try
            {
                var sourceFiles = HiveFinder.FindHivesInStandardLocations();

                if (!sourceFiles.Any())
                {
                    return new ExtractionResult { Entries = new List<RegistryEntry>(), ProcessedHives = new List<string>() };
                }

                progress?.Report("Copying hives via VSS...");
                var tempHivePaths = await CopyHives.CopyHivesToTempAsync(sourceFiles, progress).ConfigureAwait(false);

                hivePaths = tempHivePaths.Values;
                progress?.Report($"Processing {hivePaths.Count()} hives from the local cache.");

                // Run independent parsers in parallel to reduce overall runtime
                progress?.Report("Starting background parsers...");

                // Wrap each task with robust result handling to ensure no failures drop entire result sets
                var prefetchTask = Task.Run(() => PrefetchParser.ParsePrefetchFiles(null), cancellationToken)
                    .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);
                
                var pcaTask = Task.Run(() => PCAParser.ParsePCADatabase(null), cancellationToken)
                    .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);
                
                var werTask = Task.Run(() => WERParser.ParseWERReports(null), cancellationToken)
                    .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);
                
                var eventLogTask = Task.Run(() => EventLogParser.ParseEventLogs(null), cancellationToken)
                    .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);
                
                var detectionTask = Task.Run(() => DetectionHistoryParser.ParseDetectionHistory(null), cancellationToken)
                    .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);
                
                var recentItemsTask = Task.Run(() => RecentItemsParser.ParseRecentItems(null), cancellationToken)
                    .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);

                // Shimcache depends on SYSTEM hive file path
                string systemHivePath = null;
                if (tempHivePaths.ContainsKey("SYSTEM"))
                {
                    systemHivePath = tempHivePaths["SYSTEM"];
                }

                Task<List<RegistryEntry>> shimcacheTask;
                if (string.IsNullOrEmpty(systemHivePath))
                {
                    shimcacheTask = Task.FromResult(new List<RegistryEntry>());
                }
                else
                {
                    shimcacheTask = ShimcacheParser.ParseShimcacheAsync(systemHivePath, null, cancellationToken)
                        .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);
                }

                // Amcache may or may not exist on disk
                var windowsRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                var amcachePath = Path.Combine(windowsRoot, @"appcompat\Programs\Amcache.hve");

                Task<List<RegistryEntry>> amcacheTask;
                if (File.Exists(amcachePath))
                {
                    amcacheTask = AmcacheParser.ParseAmcacheFileAsync(amcachePath, null, cancellationToken)
                        .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<RegistryEntry>(), TaskContinuationOptions.ExecuteSynchronously);
                }
                else
                {
                    amcacheTask = Task.FromResult(new List<RegistryEntry>());
                }

                var allHiveResultsTask = ExtractFromHivesInternalAsync(hivePaths, progress, cancellationToken)
                    .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : new List<List<RegistryEntry>>(), TaskContinuationOptions.ExecuteSynchronously);

                await Task.WhenAll(prefetchTask, pcaTask, werTask, eventLogTask, detectionTask, recentItemsTask, shimcacheTask, amcacheTask, allHiveResultsTask).ConfigureAwait(false);

                var prefetchEntries = await prefetchTask;
                var pcaEntries = await pcaTask;
                var werEntries = await werTask;
                var eventLogEntries = await eventLogTask;
                var detectionHistoryEntries = await detectionTask;
                var recentItemsEntries = await recentItemsTask;
                var shimcacheEntries = await shimcacheTask;
                var amcacheEntries = await amcacheTask;

                // Merge non-registry parser results into the master list
                var allParserResults = await allHiveResultsTask;

                if (prefetchEntries != null && prefetchEntries.Count > 0)
                {
                    allParserResults.Add(prefetchEntries);
                }

                if (pcaEntries != null && pcaEntries.Count > 0)
                {
                    allParserResults.Add(pcaEntries);
                }

                if (werEntries != null && werEntries.Count > 0)
                {
                    allParserResults.Add(werEntries);
                }

                if (eventLogEntries != null && eventLogEntries.Count > 0)
                {
                    allParserResults.Add(eventLogEntries);
                }

                if (detectionHistoryEntries != null && detectionHistoryEntries.Count > 0)
                {
                    allParserResults.Add(detectionHistoryEntries);
                }

                if (shimcacheEntries != null && shimcacheEntries.Count > 0)
                {
                    allParserResults.Add(shimcacheEntries);
                }

                if (amcacheEntries != null && amcacheEntries.Count > 0)
                {
                    allParserResults.Add(amcacheEntries);
                }

                if (recentItemsEntries != null && recentItemsEntries.Count > 0)
                {
                    allParserResults.Add(recentItemsEntries);
                }

                progress?.Report("Aggregating and validating results...");
                var processedHives = GetProcessedHiveNames(hivePaths);

                finalEntries = await EvidenceAggregator.AggregateAllEvidenceAsync(allParserResults, uppercaseResults, progress).ConfigureAwait(false);
                
                // Clear allParserResults - it's already been aggregated into finalEntries
                // Note: EvidenceAggregator.AggregateAllEvidenceAsync already clears this, but be explicit
                allParserResults.Clear();
                allParserResults = null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Intentionally swallow exceptions to allow best-effort extraction
            }

            return new ExtractionResult { Entries = finalEntries, ProcessedHives = GetProcessedHiveNames(hivePaths) };
        }

        private static async Task<List<List<RegistryEntry>>> ExtractFromHivesInternalAsync(IEnumerable<string> hivePaths, IProgress<string> progress, CancellationToken cancellationToken = default)
        {
            var allParserResults = new List<List<RegistryEntry>>();

            var hivePathsList = hivePaths.ToList();

            if (hivePathsList.Count == 0)
            {
                return allParserResults;
            }

            // Launch processing for all hives and rely on ParserSemaphore inside ProcessSingleHiveAsync to throttle
            var tasks = hivePathsList.Select(path => ProcessSingleHiveAsync(path, cancellationToken)).ToList();

            progress?.Report($"Processing {tasks.Count} hives...");

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var result in results)
            {
                if (result.Entries != null && result.Entries.Any())
                {
                    allParserResults.Add(result.Entries);
                }
            }

            return allParserResults;
        }

        public static async Task<ExtractionResult> ExtractFromHivesAsync(IEnumerable<string> hivePaths, IProgress<string> progress, Action<string> _, CancellationToken cancellationToken = default, bool uppercaseResults = true)
        {
            var allParserResults = await ExtractFromHivesInternalAsync(hivePaths, progress, cancellationToken).ConfigureAwait(false);
            var processedHives = GetProcessedHiveNames(hivePaths);
            var finalEntries = await EvidenceAggregator.AggregateAllEvidenceAsync(allParserResults, uppercaseResults, progress).ConfigureAwait(false);
            
            return new ExtractionResult { Entries = finalEntries, ProcessedHives = processedHives };
        }

        private static List<string> GetProcessedHiveNames(IEnumerable<string> hivePaths)
        {
            var processedHives = new List<string>();
            foreach (var path in hivePaths)
            {
                var fileName = Path.GetFileName(path);
                processedHives.Add(fileName);
            }
            return processedHives;
        }

        private static async Task<(List<RegistryEntry> Entries, HiveType HiveType, string Path)> ProcessSingleHiveAsync(string hivePath, CancellationToken cancellationToken)
        {
            await ParserSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                var entries = new List<RegistryEntry>(1000);
                var fileName = Path.GetFileName(hivePath);

                cancellationToken.ThrowIfCancellationRequested();

                var hive = new RegistryHive(hivePath);
                var hiveType = GetHiveType(hive, fileName);

                // Parse hive on threadpool
                await Task.Run(() => hive.ParseHive(), cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (hive.Root != null)
                {
                    var genericEntries = await GenericParser.ParseHiveAsync(hive, hiveType, null, cancellationToken);
                    entries.AddRange(genericEntries);
                }

                cancellationToken.ThrowIfCancellationRequested();

                switch (hiveType)
                {
                    case HiveType.NTUSER:
                    case HiveType.USRCLASS:
                        var shellbagEntries = await ShellbagParser.ParseShellbagsAsync(hive, null, cancellationToken, null);
                        entries.AddRange(shellbagEntries);
                        break;
                    case HiveType.SYSTEM:
                        int beforeBAM = entries.Count;
                        await ProcessSystemHiveAsync(hive, entries, cancellationToken);
                        break;
                }

                return (entries, hiveType, hivePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (new List<RegistryEntry>(), HiveType.SYSTEM, hivePath);
            }
            finally
            {
                ParserSemaphore.Release();
            }
        }

        private static async Task ProcessSystemHiveAsync(RegistryHive hive, List<RegistryEntry> entries, CancellationToken cancellationToken)
        {
            if (hive.Root == null) 
                await Task.Run(() => hive.ParseHive(), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var controlSets = new List<string>();
            var selectKey = hive.GetKey("Select");
            if (selectKey != null)
            {
                var currentControlSet = selectKey.Values?.FirstOrDefault(v => v.ValueName == "Current");
                if (currentControlSet != null && !string.IsNullOrEmpty(currentControlSet.ValueData))
                {
                    try
                    {
                        var csName = $"ControlSet{int.Parse(currentControlSet.ValueData):D3}";
                        controlSets.Add(csName);
                    }
                    catch { }
                }
            }

            if (!controlSets.Any())
            {
                var rootSubKeys = hive.Root?.SubKeys?.Where(k => k.KeyName.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase)).ToList();
                if (rootSubKeys != null && rootSubKeys.Any())
                {
                    controlSets.AddRange(rootSubKeys.Select(k => k.KeyName).OrderByDescending(x => x));
                }
            }

            if (!controlSets.Any())
            {
                controlSets.Add("ControlSet001");
            }

            foreach (var controlSet in controlSets.Take(2))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bamTasks = new List<Task<List<RegistryEntry>>>();

                string bamStatePath = $@"{controlSet}\Services\bam\State\UserSettings";
                var bamStateKey = hive.GetKey(bamStatePath);
                if (bamStateKey != null)
                {
                    bamTasks.Add(BAMParser.ParseBAMKeyAsync(bamStateKey, null, cancellationToken));
                }

                string bamUserPath = $@"{controlSet}\Services\bam\UserSettings";
                var bamUserKey = hive.GetKey(bamUserPath);
                if (bamUserKey != null)
                {
                    bamTasks.Add(BAMParser.ParseBAMKeyAsync(bamUserKey, null, cancellationToken));
                }

                if (bamTasks.Count > 0)
                {
                    var bamResults = await Task.WhenAll(bamTasks);
                    
                    int totalBAM = 0;
                    foreach (var result in bamResults)
                    {
                        entries.AddRange(result);
                        totalBAM += result.Count;
                    }
                    
                    if (bamResults.Any(r => r.Count > 0))
                        break;
                }
            }
        }

        private static HiveType GetHiveType(RegistryHive hive, string fileName)
        {
            var upperFileName = fileName.ToUpperInvariant();

            if (upperFileName.StartsWith("NTUSER.DAT"))
                return HiveType.NTUSER;
            if (upperFileName.StartsWith("USRCLASS.DAT"))
                return HiveType.USRCLASS;
            if (upperFileName.Equals("SOFTWARE"))
                return HiveType.SOFTWARE;
            if (upperFileName.Equals("SYSTEM"))
                return HiveType.SYSTEM;

            if (hive.Root != null)
            {
                if (hive.Root.KeyName.Equals("CMI-CreateHive{C26B4A42-A23A-4550-A018-3333A528C5D8}", StringComparison.OrdinalIgnoreCase))
                    return HiveType.SOFTWARE;
                if (hive.Root.SubKeys.Any(sk => sk.KeyName.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase)))
                    return HiveType.SYSTEM;
            }

            return HiveType.SYSTEM;
        }
    }
}