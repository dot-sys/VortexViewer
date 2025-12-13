using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;

// Exports registry hives using VSS
namespace Timeline.Core.Util
{
    // Requires admin privileges for hive export
    public static class CopyHives
    {
        #region Win32 API Declarations

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr ProcessHandle,
            uint DesiredAccess,
            out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(
            string lpSystemName,
            string lpName,
            out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            uint BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID Luid;
            public uint Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_BACKUP_NAME = "SeBackupPrivilege";
        private const string SE_RESTORE_NAME = "SeRestorePrivilege";

        #endregion

        private static bool _privilegesEnabled = false;
        private static readonly object _privilegeLock = new object();

        public static async Task<Dictionary<string, string>> CopyHivesToTempAsync(Dictionary<string, string> sourceFiles, IProgress<string> progress)
        {
            if (!sourceFiles.Any())
            {
                return new Dictionary<string, string>();
            }

            var tempFilePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tempDirectory = Path.Combine(Path.GetTempPath(), "VortexHives_" + Guid.NewGuid().ToString("N"));

            try
            {
                if (!_privilegesEnabled)
                {
                    lock (_privilegeLock)
                    {
                        if (!_privilegesEnabled)
                        {
                            if (!EnableBackupPrivileges())
                            {
                                progress?.Report("Warning: Backup privileges not enabled");
                            }
                            else
                            {
                                _privilegesEnabled = true;
                            }
                        }
                    }
                }

                Directory.CreateDirectory(tempDirectory);

                int index = 0;
                foreach (var sourceFile in sourceFiles)
                {
                    index++;
                    var hiveName = sourceFile.Key;
                    var livePath = sourceFile.Value;

                    progress?.Report($"Processing {hiveName} ({index}/{sourceFiles.Count})...");

                    var (name, path, success) = await ProcessSingleHiveAsync(hiveName, livePath, tempDirectory, progress).ConfigureAwait(false);
                    
                    if (success && !string.IsNullOrEmpty(path))
                    {
                        tempFilePaths[name] = path;
                    }
                }

                if (!tempFilePaths.Any())
                {
                    throw new InvalidOperationException("Failed to export any registry hives. Ensure the application is running with administrator privileges.");
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Error: {ex.Message}");
                
                try
                {
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                }
                catch
                {
                }

                throw new InvalidOperationException("Failed during registry export. Ensure the application has administrator rights.", ex);
            }

            return tempFilePaths;
        }

        private static bool EnableBackupPrivileges()
        {
            try
            {
                IntPtr tokenHandle = IntPtr.Zero;
                
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                {
                    return false;
                }

                try
                {
                    if (!EnablePrivilege(tokenHandle, SE_BACKUP_NAME))
                    {
                        return false;
                    }

                    if (!EnablePrivilege(tokenHandle, SE_RESTORE_NAME))
                    {
                        return false;
                    }

                    return true;
                }
                finally
                {
                    CloseHandle(tokenHandle);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool EnablePrivilege(IntPtr tokenHandle, string privilegeName)
        {
            LUID luid;
            if (!LookupPrivilegeValue(null, privilegeName, out luid))
            {
                return false;
            }

            TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };

            bool result = AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            return result;
        }

        private static async Task<(string hiveName, string tempPath, bool success)> ProcessSingleHiveAsync(
            string hiveName, 
            string livePath, 
            string tempDirectory, 
            IProgress<string> progress)
        {
            try
            {
                var tempPath = Path.Combine(tempDirectory, hiveName);

                var result = await Task.Run(() =>
                {
                    if (!File.Exists(livePath))
                    {
                        progress?.Report($"Error: {hiveName} not found at {livePath}");
                        return (hiveName, null, false);
                    }
                    
                    if (RegSaveCommand(hiveName, livePath, tempPath, progress))
                    {
                        return (hiveName, tempPath, true);
                    }
                    
                    return (hiveName, null, false);
                }).ConfigureAwait(false);

                return result;
            }
            catch (Exception)
            {
                progress?.Report($"Error: {hiveName}");
                return (hiveName, null, false);
            }
        }

        private static bool RegSaveCommand(string hiveName, string sourcePath, string destPath, IProgress<string> progress)
        {
            try
            {
                string regKeyPath = GetRegistryKeyPath(hiveName);
                if (string.IsNullOrEmpty(regKeyPath))
                {
                    progress?.Report($"Warning: No registry key path found for {hiveName}");
                    return false;
                }


                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"save \"{regKeyPath}\" \"{destPath}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };


                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    
                    process.WaitForExit(30000);
                    
                    if (!process.HasExited)
                    {
                        try { process.Kill(); } catch { }
                        progress?.Report($"Timeout saving {hiveName}");
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                    }
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        progress?.Report($"Error saving {hiveName}: {error.Trim()}");
                    }

                    if (process.ExitCode == 0 && File.Exists(destPath))
                    {
                        var fileInfo = new FileInfo(destPath);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Exception saving {hiveName}: {ex.Message}");
                return false;
            }
        }

        // Only handles currently loaded hives
        private static string GetRegistryKeyPath(string hiveName)
        {
            var upperHiveName = hiveName.ToUpperInvariant();
            
            if (upperHiveName == "SYSTEM")
                return "HKLM\\SYSTEM";
            if (upperHiveName == "SOFTWARE")
                return "HKLM\\SOFTWARE";
            if (upperHiveName == "SAM")
                return "HKLM\\SAM";
            if (upperHiveName == "SECURITY")
                return "HKLM\\SECURITY";
            
            if (upperHiveName.StartsWith("NTUSER.DAT"))
            {
                string username = null;
                if (hiveName.Contains("_"))
                {
                    username = hiveName.Substring(hiveName.IndexOf('_') + 1);
                }
                
                string currentUser = Environment.UserName;
                if (!string.IsNullOrEmpty(username) && username.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    return "HKCU";
                }
                
                return null;
            }
            
            if (upperHiveName.StartsWith("USRCLASS.DAT"))
            {
                string username = null;
                if (hiveName.Contains("_"))
                {
                    username = hiveName.Substring(hiveName.IndexOf('_') + 1);
                }
                
                string currentUser = Environment.UserName;
                if (!string.IsNullOrEmpty(username) && username.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                {
                    return "HKCU\\Software\\Classes";
                }
                
                return null;
            }
            
            return null;
        }
    }
}
