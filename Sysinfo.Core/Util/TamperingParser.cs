using System;
using System.IO;
using System.Linq;
using System.Management;
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

        // Gets SRUM database creation date using WMI
        private static string GetSrumCreatedDate()
        {
            var filePath = @"C:\Windows\System32\sru\SRUDB.dat";
            
            var result = GetFileCreationDateViaWMI(filePath);
            if (result != null)
            {
                return result;
            }
            
            return "Unavailable";
        }

        // Gets AmCache creation date using WMI
        private static string GetAmCacheCreatedDate()
        {
            var filePath = @"C:\Windows\AppCompat\Programs\Amcache.hve";
            
            var result = GetFileCreationDateViaWMI(filePath);
            if (result != null)
            {
                return result;
            }
            
            return "Unavailable";
        }

        // Gets Defender event log creation date using WMI
        private static string GetDefenderEventLogCreatedDate()
        {
            try
            {
                var query = $"SELECT * FROM CIM_DataFile WHERE Drive='C:' AND Path='\\\\Windows\\\\System32\\\\winevt\\\\Logs\\\\' AND Extension='evtx' AND FileName LIKE '%Defender%Operational%'";
                
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject file in searcher.Get().Cast<ManagementObject>())
                    {
                        var fileName = file["FileName"]?.ToString();
                        var extension = file["Extension"]?.ToString();
                        var fullName = $"{fileName}.{extension}";
                        var fullPath = file["Name"]?.ToString();
                        
                        if (fileName != null && fileName.ToLowerInvariant().Contains("defender") && 
                            fileName.ToLowerInvariant().Contains("operational"))
                        {
                            var creationDate = file["CreationDate"]?.ToString();
                            if (!string.IsNullOrEmpty(creationDate))
                            {
                                var parsedDate = ManagementDateTimeConverter.ToDateTime(creationDate);
                                var result = parsedDate.ToString(DateFormat);
                                return result;
                            }
                        }
                    }
                }
                
                // Fallback: try pattern matching with broader search
                query = "SELECT * FROM CIM_DataFile WHERE Drive='C:' AND Path='\\\\Windows\\\\System32\\\\winevt\\\\Logs\\\\' AND Extension='evtx'";
                
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject file in searcher.Get().Cast<ManagementObject>())
                    {
                        var fullPath = file["Name"]?.ToString();
                        if (fullPath != null && fullPath.ToLowerInvariant().Contains("defender") && 
                            fullPath.ToLowerInvariant().Contains("operational"))
                        {
                            var creationDate = file["CreationDate"]?.ToString();
                            if (!string.IsNullOrEmpty(creationDate))
                            {
                                var parsedDate = ManagementDateTimeConverter.ToDateTime(creationDate);
                                var result = parsedDate.ToString(DateFormat);
                                return result;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            
            return "Unavailable";
        }

        // Gets file creation date using WMI for protected system files
        private static string GetFileCreationDateViaWMI(string filePath)
        {
            try
            {
                // Normalize path for WMI query
                var normalizedPath = filePath.Replace("/", "\\")
                    .TrimStart(new char[] { '\\', 'C', ':' });

                var pathParts = normalizedPath.Split('\\');
                var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
                var extension = Path.GetExtension(normalizedPath).TrimStart('.');
                var directory = string.Join("\\\\", pathParts.Take(pathParts.Length - 1));
                
                var query = $"SELECT CreationDate FROM CIM_DataFile WHERE Drive='C:' AND Path='\\\\{directory}\\\\' AND FileName='{fileName}' AND Extension='{extension}'";
                
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject file in searcher.Get().Cast<ManagementObject>())
                    {
                        var creationDate = file["CreationDate"]?.ToString();
                        if (!string.IsNullOrEmpty(creationDate))
                        {
                            var parsedDate = ManagementDateTimeConverter.ToDateTime(creationDate);
                            return parsedDate.ToString(DateFormat);
                        }
                    }
                }
            }
            catch
            {
            }
            
            return null;
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
            var serviceStatus = "Disabled";
            
            try
            {
                // Check VSS service status using WMI
                var query = "SELECT State FROM Win32_Service WHERE Name='VSS'";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject service in searcher.Get().Cast<ManagementObject>())
                    {
                        var state = service["State"]?.ToString();
                        
                        if (state != null && state.Equals("Running", StringComparison.OrdinalIgnoreCase))
                        {
                            serviceStatus = "Enabled";
                        }
                        break;
                    }
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
                // WMI query to find snap*.vhd files in System Volume Information
                var query = "SELECT Name, CreationDate FROM CIM_DataFile WHERE Drive='C:' AND Path='\\\\System Volume Information\\\\' AND Extension='vhd' AND FileName LIKE 'snap%'";
                
                DateTime? newestDate = null;
                string newestFile = null;
                
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject file in searcher.Get().Cast<ManagementObject>())
                    {
                        var fileName = file["Name"]?.ToString();
                        var creationDate = file["CreationDate"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(creationDate))
                        {
                            var parsedDate = ManagementDateTimeConverter.ToDateTime(creationDate);
                            
                            if (!newestDate.HasValue || parsedDate > newestDate.Value)
                            {
                                newestDate = parsedDate;
                                newestFile = fileName;
                            }
                        }
                    }
                }
                
                if (newestDate.HasValue)
                {
                    var result = newestDate.Value.ToString(DateFormat);
                    return result;
                }
            }
            catch
            {
            }
            
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
