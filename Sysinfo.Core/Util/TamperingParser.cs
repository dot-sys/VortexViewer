using System;
using System.IO;
using System.Linq;
using SysInfo.Core.Models;

// Anti-forensics detection and artifact analysis utilities
namespace SysInfo.Core.Util
{
    // Analyzes system artifacts for tampering indicators
    public static class TamperingParser
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        // Collects all tampering indicators into single object
        public static TamperingInfo GetTamperingInfo()
        {
            var info = new TamperingInfo();

            try
            {
                info.SrumCreatedDate = GetFileCreationDate(@"\Windows\System32\sru\SRUDB.dat");
                info.AmCacheCreatedDate = GetFileCreationDate(@"\Windows\AppCompat\Programs\Amcache.hve");
                info.DefenderEventLogCreatedDate = GetFileCreationDate(@"\Windows\System32\winevt\Logs\Microsoft-Windows-Windows Defender%4Operational.evtx");
                info.LastRecycleBinDeletion = GetLastRecycleBinDeletion();
                info.VolumeShadowCopies = GetVolumeShadowCopiesStatus();
                GetPrefetchInfo(out string oldestFile, out string totalCount);
                info.OldestPrefetchFile = oldestFile;
                info.PrefetchTotalCount = totalCount;
            }
            catch (Exception ex)
            {
                info.SrumCreatedDate = $"Error: {ex.Message}";
            }

            return info;
        }

        // Returns system drive letter from environment variable
        private static string GetSystemDrive()
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (string.IsNullOrEmpty(systemDrive))
                return "C:";
            if (!systemDrive.EndsWith(":"))
                systemDrive += ":";
            return systemDrive;
        }

        // Gets file creation timestamp from system path
        private static string GetFileCreationDate(string relativePath)
        {
            try
            {
                var fullPath = GetSystemDrive() + relativePath;
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    return fileInfo.CreationTime.ToString(DateFormat);
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Finds most recent recycle bin deletion timestamp
        private static string GetLastRecycleBinDeletion()
        {
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .ToList();

                DateTime? latestDeletion = null;

                foreach (var drive in drives)
                {
                    var recycleBinPath = Path.Combine(drive.Name, "$Recycle.Bin");
                    
                    if (Directory.Exists(recycleBinPath))
                    {
                        try
                        {
                            var lastWrite = Directory.GetLastWriteTime(recycleBinPath);
                            if (!latestDeletion.HasValue || lastWrite > latestDeletion.Value)
                                latestDeletion = lastWrite;
                        }
                        catch
                        {
                        }
                    }
                }

                if (latestDeletion.HasValue)
                    return latestDeletion.Value.ToString(DateFormat);
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Checks volume shadow copy service status
        private static string GetVolumeShadowCopiesStatus()
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "vssadmin.exe",
                        Arguments = "list shadows",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (output.Contains("No items found"))
                    return "Disabled / No Shadows";

                if (output.Contains("Shadow Copy Volume"))
                {
                    var count = output.Split(new[] { "Shadow Copy Volume" }, StringSplitOptions.None).Length - 1;
                    return $"Enabled ({count} shadow copies)";
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Retrieves oldest prefetch file and total count
        private static void GetPrefetchInfo(out string oldestFile, out string totalCount)
        {
            oldestFile = "Unavailable";
            totalCount = "Unavailable";

            try
            {
                var prefetchPath = GetSystemDrive() + @"\Windows\Prefetch";
                
                if (Directory.Exists(prefetchPath))
                {
                    var pfFiles = Directory.GetFiles(prefetchPath, "*.pf");
                    totalCount = pfFiles.Length.ToString();
                    
                    if (pfFiles.Length > 0)
                    {
                        var oldest = pfFiles
                            .Select(f => new { Path = f, Created = new FileInfo(f).CreationTime })
                            .OrderBy(x => x.Created)
                            .First();

                        var fileName = Path.GetFileName(oldest.Path);
                        oldestFile = $"{fileName} ({oldest.Created:dd/MM/yyyy})";
                    }
                }
            }
            catch
            {
            }
        }
    }
}
