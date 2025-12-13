using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Processes.Core.Models;

/// Process memory analysis utilities
namespace Processes.Core.Util
{
    /// Extracts executable paths from PCA traces
    public static class PCAExtractor
    {
        /// Regex pattern for executable file paths
        private static readonly Regex PathRegex = new Regex(
            @"[a-zA-Z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+\.exe",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// Extracts PCA traces from target processes
        public static async Task<List<(string Path, string Source)>> ExtractPCATracesAsync(
            IProgress<string> progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<(string Path, string Source)>();
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            progress?.Report("Finding target processes...");

            var allProcesses = ReadProcesses.GetRunningProcesses();
            var targetGroups = GetTargetProcessGroups(allProcesses);
            int totalProcesses = targetGroups.Sum(g => g.processes.Count);

            if (totalProcesses == 0)
            {
                progress?.Report("No target processes found");
                return results;
            }

            progress?.Report($"Found {totalProcesses} target process(es)");
            int processedCount = 0;

            foreach (var (sourceName, processes) in targetGroups)
            {
                foreach (var proc in processes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"Processing {sourceName} (PID: {proc.Id})...");

                    var paths = await ExtractPathsFromProcessAsync(proc.Id, sourceName, cancellationToken);
                    foreach (var path in paths)
                    {
                        if (uniquePaths.Add(path))
                        {
                            results.Add((path, sourceName));
                        }
                    }

                    processedCount++;
                    progress?.Report($"Processed {processedCount}/{totalProcesses} processes");
                }
            }

            progress?.Report($"Extraction complete - found {results.Count} unique paths");
            return results;
        }

        /// Groups processes by PCA source type
        private static List<(string sourceName, List<ProcessInfo> processes)> GetTargetProcessGroups(List<ProcessInfo> allProcesses)
        {
            return new List<(string, List<ProcessInfo>)>
            {
                ("Explorer.exe", allProcesses.Where(p => p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase)).ToList()),
                ("RuntimeBroker.exe", allProcesses.Where(p => p.Name.Equals("RuntimeBroker", StringComparison.OrdinalIgnoreCase)).ToList()),
                ("PCASVC", allProcesses.Where(p => p.ServiceName != null && p.ServiceName.Equals("PcaSvc", StringComparison.OrdinalIgnoreCase)).ToList())
            };
        }

        /// Scans process memory for trace paths
        private static async Task<List<string>> ExtractPathsFromProcessAsync(
            int processId,
            string sourceName,
            CancellationToken cancellationToken)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var result = await ReadMemory.GetStringsFromProcessAsync(
                    processId,
                    minLength: 4,
                    asciiOnly: false,
                    cancellationToken
                );

                var traceStrings = result.Strings
                    .Where(s => s.IndexOf("trace,", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();


                foreach (var traceString in traceStrings)
                {
                    var matches = PathRegex.Matches(traceString);
                    foreach (Match match in matches)
                    {
                        if (match.Success)
                        {
                            paths.Add(match.Value);
                        }
                    }
                }

            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
            }

            return paths.ToList();
        }
    }
}
