using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Timeline.Core.Models;

// Validates file existence on paths
namespace Timeline.Core.Util
{
    // Appends Present/Deleted/Unknown status
    public static class StatusCheck
    {
        private static readonly Regex WindowsPathRegex = new Regex(
            @"(?:""?[a-zA-Z]\:|\\\\[^\\\/\:\*\?\<\>\|]+\\[^\\\/\:\*\?\<\>\|]*)\\(?:[^\\\/\:\*\?\<\>\|]+\\)*\w([^\\\/\:\*\?\<\>\|])*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private const int MAX_CONCURRENT_CHECKS = 8;
        private const int CHECK_TIMEOUT_MS = 100;
        private const int BATCH_SIZE = 500;

        public static async Task CheckAllPathStatusAsync(List<RegistryEntry> entries, IProgress<string> progress = null)
        {
            if (entries == null || entries.Count == 0)
                return;

            int totalCount = entries.Count;
            progress?.Report("Checking file/folder status...");

            using (var semaphore = new SemaphoreSlim(MAX_CONCURRENT_CHECKS, MAX_CONCURRENT_CHECKS))
            {
                var tasks = new List<Task>();
                int processedCount = 0;

                foreach (var entry in entries)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var status = CheckPathStatus(entry.Path);
                            
                            if (!string.IsNullOrEmpty(status))
                            {
                                if (string.IsNullOrEmpty(entry.OtherInfo))
                                {
                                    entry.OtherInfo = status;
                                }
                                else
                                {
                                    entry.OtherInfo += "; " + status;
                                }
                            }

                            Interlocked.Increment(ref processedCount);
                            
                            if (processedCount % BATCH_SIZE == 0)
                            {
                                progress?.Report($"Checked {processedCount}/{totalCount} paths...");
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));

                    if (tasks.Count >= BATCH_SIZE)
                    {
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                        tasks.Clear();
                    }
                }

                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
            }

            progress?.Report($"File status check complete ({totalCount} paths)");
        }

        private static string CheckPathStatus(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (!WindowsPathRegex.IsMatch(path))
                return "Unknown";

            try
            {
                if (File.Exists(path))
                    return "Present";

                if (Directory.Exists(path))
                    return "Present";

                return "Deleted";
            }
            catch
            {
                return "Deleted";
            }
        }

        [Obsolete("Use CheckAllPathStatusAsync instead to avoid blocking")]
        public static void CheckAllPathStatus(List<RegistryEntry> entries)
        {
            Task.Run(async () => await CheckAllPathStatusAsync(entries, null)).GetAwaiter().GetResult();
        }
    }
}
