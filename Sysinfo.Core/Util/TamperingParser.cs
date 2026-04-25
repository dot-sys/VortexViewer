using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using SysInfo.Core.Models;

// Anti-forensics detection and artifact analysis utilities
namespace SysInfo.Core.Util
{
    // Analyzes system artifacts for tampering indicators
    public static class TamperingParser
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint GetFileAttributes(string lpFileName);

        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        // Collects all tampering indicators into single object
        public static TamperingInfo GetTamperingInfo()
        {
            var info = new TamperingInfo();

            try
            {
                info.SrumCreatedDate = GetSrumCreatedDate();
                info.AmCacheCreatedDate = GetAmCacheCreatedDate();
                info.DefenderEventLogCreatedDate = GetDefenderEventLogCreatedDate();
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
                return "C:\\";
            if (!systemDrive.EndsWith(":\\"))
            {
                if (!systemDrive.EndsWith(":"))
                    systemDrive += ":";
                systemDrive += "\\";
            }
            return systemDrive;
        }

        // Resolves System32 path with Sysnative bypass for 32-bit processes
        private static string GetSystem32Path(string relativePath)
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var system32 = Path.Combine(windowsDir, "System32");
            
            // Check if we are 32-bit process on 64-bit OS (Sysnative only exists in 32-bit processes)
            if (IntPtr.Size == 4)
            {
                var sysnative = Path.Combine(windowsDir, "Sysnative");
                if (Directory.Exists(sysnative))
                {
                    system32 = sysnative;
                }
            }
            
            return Path.Combine(system32, relativePath);
        }

        // Gets SRUM database creation date using WMI
        private static string GetSrumCreatedDate()
        {
            var filePath = GetSystem32Path(@"sru\SRUDB.dat");
            return GetFileCreationDateNative(filePath);
        }

        // Gets AmCache creation date using WMI
        private static string GetAmCacheCreatedDate()
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var filePath = Path.Combine(windowsDir, @"AppCompat\Programs\Amcache.hve");
            return GetFileCreationDateNative(filePath);
        }

        // Gets Defender event log creation date using WMI
        private static string GetDefenderEventLogCreatedDate()
        {
            try
            {
                var logDir = GetSystem32Path(@"winevt\Logs");
                if (Directory.Exists(logDir))
                {
                    var files = Directory.GetFiles(logDir, "*Defender*Operational*.evtx");
                    if (files.Length > 0)
                    {
                        var info = new FileInfo(files[0]);
                        return info.CreationTime.ToString(DateFormat);
                    }
                }
            }
            catch { }
            
            return "Unavailable";
        }

        // Gets file creation date using WMI for protected system files
        private static string GetFileCreationDateNative(string filePath)
        {
            try
            {
                if (GetFileAttributes(filePath) != INVALID_FILE_ATTRIBUTES)
                {
                    var info = new FileInfo(filePath);
                    return info.CreationTime.ToString(DateFormat);
                }
            }
            catch { }
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

        // Checks volume shadow copy service status and finds newest snapshot
        private static string GetVolumeShadowCopiesStatus()
        {
            var serviceStatus = "Unknown";
            
            try
            {
                // Native check: system drive access for VSS artifacts
                var sysVolInfo = Path.Combine(GetSystemDrive(), "System Volume Information");
                if (Directory.Exists(sysVolInfo))
                {
                    serviceStatus = "Running/Active";
                }
                else
                {
                    serviceStatus = "Disabled/Inaccessible";
                }
            }
            catch
            {
            }
            
            // Find newest snap*.vhd file in System Volume Information
            var newestSnapDate = GetNewestShadowCopySnapshot();
            
            if (!string.IsNullOrEmpty(newestSnapDate))
            {
                return $"{serviceStatus} (Newest: {newestSnapDate})";
            }
            
            return serviceStatus;
        }

        // Gets newest shadow copy snapshot file creation date using WMI
        private static string GetNewestShadowCopySnapshot()
        {
            try
            {
                var sysVolInfo = Path.Combine(GetSystemDrive(), "System Volume Information");
                if (Directory.Exists(sysVolInfo))
                {
                    var files = Directory.GetFiles(sysVolInfo, "snap*.vhd");
                    if (files.Length > 0)
                    {
                        var newest = files
                            .Select(f => new FileInfo(f))
                            .OrderByDescending(f => f.CreationTime)
                            .First();
                        return newest.CreationTime.ToString(DateFormat);
                    }
                }
            }
            catch { }
            
            return null;
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
                        oldestFile = $"{fileName} ({oldest.Created.ToString(DateFormat)})";
                    }
                }
            }
            catch
            {
            }
        }
    }
}
