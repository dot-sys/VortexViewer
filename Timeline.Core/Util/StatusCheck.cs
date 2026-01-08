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
        // Simplified regex that focuses on drive letter or UNC paths
        private static readonly Regex WindowsPathRegex = new Regex(
            @"^(?:[a-zA-Z]:\\|\\\\)",
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

            // More robust validation: check if it's a valid Windows path
            if (!IsValidWindowsPath(path))
                return "Unknown";

            try
            {
                if (File.Exists(path))
                    return "Present";

                if (Directory.Exists(path))
                    return "Present";

                return "Deleted";
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // CryptographicException thrown when accessing corrupted registry/shell bag data
                // or when ExtensionBlocks library encounters malformed binary data
                return "Unknown";
            }
            catch (UnauthorizedAccessException)
            {
                // Permission denied - file may exist but we can't access it
                return "Unknown";
            }
            catch (System.Net.NetworkInformation.NetworkInformationException)
            {
                // Network path unavailable
                return "Unknown";
            }
            catch (IOException)
            {
                // I/O error (network timeout, device not ready, etc.)
                return "Unknown";
            }
            catch
            {
                // Don't return "Deleted" on exceptions - return "Unknown" instead
                return "Unknown";
            }
        }

        private static bool IsValidWindowsPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 3)
                return false;

            // Check for drive letter path (C:\...)
            if (char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\')
                return true;

            // Check for UNC path (\\server\share)
            if (path.StartsWith(@"\\"))
                return true;

            return false;
        }

        [Obsolete("Use CheckAllPathStatusAsync instead to avoid blocking")]
        public static void CheckAllPathStatus(List<RegistryEntry> entries)
        {
            Task.Run(async () => await CheckAllPathStatusAsync(entries, null)).GetAwaiter().GetResult();
        }
    }
}
