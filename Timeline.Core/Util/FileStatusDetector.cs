using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;

namespace Timeline.Core.Util
{
    public class SignatureInfo
    {
        public string Status { get; set; }
        
        public SignatureInfo()
        {
            Status = "Unknown";
        }
    }

    // Detects file availability and signature status
    public static class FileStatusDetector
    {
        private static readonly ConcurrentDictionary<string, SignatureInfo> _sigCache = new ConcurrentDictionary<string, SignatureInfo>(StringComparer.OrdinalIgnoreCase);
        public static string DetectModificationStatus(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "Unknown";
            if (IsRenamedPath(filePath)) return "Renamed";
            if (IsUnknownPath(filePath)) return "Unknown";
            if (IsDeletedFile(filePath)) return "Deleted";
            return string.Empty;
        }

        public static SignatureInfo ExtractSignatureInfo(string filePath, bool? fileExists = null)
        {
            if (string.IsNullOrEmpty(filePath)) return new SignatureInfo();

            if (_sigCache.TryGetValue(filePath, out var cached)) return cached;

            var info = PerformSignatureCheck(filePath, fileExists);
            _sigCache.TryAdd(filePath, info);
            return info;
        }

        private static SignatureInfo PerformSignatureCheck(string filePath, bool? fileExists)
        {
            var info = new SignatureInfo();

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".exe" && ext != ".dll" && ext != ".sys" && ext != ".msi" && ext != ".cat")
            {
                info.Status = string.Empty;
                return info;
            }

            if (IsInSystemFolder(filePath))
            {
                info.Status = "SysFolder";
                return info;
            }

            try
            {
                info.Status = VerifyFileSignature(filePath) ? "Signed" : "NotSigned";
            }
            catch
            {
                info.Status = "NotSigned";
            }

            return info;
        }

        private static bool VerifyFileSignature(string filePath)
        {
            try
            {
                using (var cert = new X509Certificate2(filePath))
                {
                    return cert != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRenamedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.IndexOf("renamed", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (path.IndexOf(".old", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(fileName)) return false;

            var ext = Path.GetExtension(path);
            return ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".old", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".backup", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnknownPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            if (path.StartsWith(":") || path.StartsWith("[")) return true;
            if (path.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("Unmapped", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("MISSING", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (path.Length >= 3)
            {
                if (!((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')))
                    return !path.StartsWith(@"\\");
                return path[1] != ':' || path[2] != '\\';
            }
            return false;
        }

        private static bool IsDeletedFile(string path)
        {
            if (string.IsNullOrEmpty(path) || IsUnknownPath(path) || path.Length < 3) return false;
            if (path.StartsWith("HKEY", StringComparison.OrdinalIgnoreCase) || path.StartsWith("HK", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                return !File.Exists(path) && !Directory.Exists(path);
            }
            catch { return false; }
        }

        private static bool IsExecutableFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".sys", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".ocx", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".cpl", StringComparison.OrdinalIgnoreCase);
        }

        public static (string Modified, SignatureInfo SignatureInfo, string PathStatus) AnalyzeFile(string filePath)
        {
            var modified = DetectModificationStatus(filePath);
            bool fileExists = false;
            string pathStatus = "Unknown";

            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        fileExists = true;
                        pathStatus = "Present";
                    }
                    else if (Directory.Exists(filePath))
                    {
                        pathStatus = "Present";
                    }
                    else if (!IsUnknownPath(filePath) && IsValidWindowsPath(filePath))
                    {
                        pathStatus = "Deleted";
                    }
                }
                catch { }
            }

            SignatureInfo signatureInfo;
            if (pathStatus == "Deleted")
            {
                signatureInfo = new SignatureInfo { Status = "Invalid" };
                modified = "Deleted";
            }
            else if (pathStatus == "Present")
            {
                signatureInfo = ExtractSignatureInfo(filePath, fileExists);
            }
            else
            {
                signatureInfo = new SignatureInfo { Status = string.Empty };
                if (string.IsNullOrEmpty(modified)) modified = "Unknown";
            }

            return (modified, signatureInfo, pathStatus);
        }

        private static bool IsValidWindowsPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 3) return false;
            if (((path[0] >= 'A' && path[0] <= 'Z' || path[0] >= 'a' && path[0] <= 'z') && path[1] == ':' && path[2] == '\\')) return true;
            return path.StartsWith(@"\\");
        }

        private static bool IsInSystemFolder(string path)
        {
            try
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLower();
                string lowerPath = path.ToLower();

                return lowerPath.StartsWith(winDir + @"\system32") ||
                       lowerPath.StartsWith(winDir + @"\syswow64") ||
                       lowerPath.StartsWith(winDir + @"\winsxs");
            }
            catch { return false; }
        }
    }
}
