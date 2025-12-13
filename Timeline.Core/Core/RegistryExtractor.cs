using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Registry;
using Timeline.Core.Models;
using Timeline.Core.Util;
using Timeline.Core.Parsers;

// Coordinate extraction and parsing of registry hives
namespace Timeline.Core.Core
{
    // Orchestrate hive export, parsing, and aggregation
    public static class RegistryExtractor
    {
        // Increased flexibility: use environment CPU count to size semaphore
        private static readonly SemaphoreSlim ParserSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
        // Make max concurrent parsers adaptive to machine (not a compile-time const)
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

                var prefetchTask = Task.Run(() => PrefetchParser.ParsePrefetchFiles(null), cancellationToken);
                var pcaTask = Task.Run(() => PCAParser.ParsePCADatabase(null), cancellationToken);
                var werTask = Task.Run(() => WERParser.ParseWERReports(null), cancellationToken);
                var eventLogTask = Task.Run(() => EventLogParser.ParseEventLogs(null), cancellationToken);
                var detectionTask = Task.Run(() => DetectionHistoryParser.ParseDetectionHistory(null), cancellationToken);
                var recentItemsTask = Task.Run(() => RecentItemsParser.ParseRecentItems(null), cancellationToken);

                // Shimcache depends on SYSTEM hive file path
                string systemHivePath = null;
                if (tempHivePaths.ContainsKey("SYSTEM"))
                {
                    systemHivePath = tempHivePaths["SYSTEM"];
                }

                Task<List<RegistryEntry>> shimcacheTask;
                if (string.IsNullOrEmpty(systemHivePath))
                {
                    shimcacheTask = Task.FromResult<List<RegistryEntry>>(null);
                }
                else
                {
                    shimcacheTask = ShimcacheParser.ParseShimcacheAsync(systemHivePath, null, cancellationToken);
                }

                // Amcache may or may not exist on disk
                var windowsRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                var amcachePath = Path.Combine(windowsRoot, @"appcompat\Programs\Amcache.hve");

                Task<List<RegistryEntry>> amcacheTask;
                if (File.Exists(amcachePath))
                {
                    amcacheTask = AmcacheParser.ParseAmcacheFileAsync(amcachePath, null, cancellationToken);
                }
                else
                {
                    amcacheTask = Task.FromResult<List<RegistryEntry>>(null);
                }

                var allHiveResultsTask = ExtractFromHivesInternalAsync(hivePaths, progress, cancellationToken);

                await Task.WhenAll(prefetchTask, pcaTask, werTask, eventLogTask, detectionTask, recentItemsTask, shimcacheTask, amcacheTask, allHiveResultsTask).ConfigureAwait(false);

                var prefetchEntries = prefetchTask.Status == TaskStatus.RanToCompletion ? prefetchTask.Result : null;
                var pcaEntries = pcaTask.Status == TaskStatus.RanToCompletion ? pcaTask.Result : null;
                var werEntries = werTask.Status == TaskStatus.RanToCompletion ? werTask.Result : null;
                var eventLogEntries = eventLogTask.Status == TaskStatus.RanToCompletion ? eventLogTask.Result : null;
                var detectionHistoryEntries = detectionTask.Status == TaskStatus.RanToCompletion ? detectionTask.Result : null;
                var recentItemsEntries = recentItemsTask.Status == TaskStatus.RanToCompletion ? recentItemsTask.Result : null;
                var shimcacheEntries = shimcacheTask.Status == TaskStatus.RanToCompletion ? shimcacheTask.Result : null;
                var amcacheEntries = amcacheTask.Status == TaskStatus.RanToCompletion ? amcacheTask.Result : null;

                // Merge non-registry parser results into the master list
                var allParserResults = allHiveResultsTask.Status == TaskStatus.RanToCompletion ? allHiveResultsTask.Result : new List<List<RegistryEntry>>();

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
                return allParserResults;

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

        public static async Task<ExtractionResult> ExtractFromHivesAsync(IEnumerable<string> hivePaths, IProgress<string> progress, Action<string> logAction, CancellationToken cancellationToken = default, bool uppercaseResults = true)
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
                        await ProcessSystemHiveAsync(hive, entries, cancellationToken);
                        break;
                }

                return (entries, hiveType, hivePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
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

            string controlSet = null;
            var selectKey = hive.GetKey("Select");
            if (selectKey != null)
            {
                var currentControlSet = selectKey.Values.FirstOrDefault(v => v.ValueName == "Current");
                if (currentControlSet != null)
                {
                    controlSet = $"ControlSet{int.Parse(currentControlSet.ValueData):D3}";
                }
            }

            if (controlSet != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Process BAM keys in parallel with throttling
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
                    
                    foreach (var result in bamResults)
                    {
                        entries.AddRange(result);
                    }
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