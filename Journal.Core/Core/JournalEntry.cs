using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

// Core journal processing and data types
namespace VortexViewer.Core
{
    // High-level journal entry with formatted data
    public struct JournalEntry
    {
        // Cached formatted timestamp string
        private readonly string _timestamp;
        // Human-readable timestamp for display
        public string Timestamp => _timestamp ?? ConvertFileTimeToString(FileTime);
        
        // Complete file path including drive
        public string FullPath { get; }
        // Human-readable reason for change
        public string ReasonString { get; }
        // Raw file time as long
        public long FileTime { get; }

        // Constructor with pre-formatted timestamp
        public JournalEntry(string timestamp, string fullPath, string reasonString, long fileTime)
        {
            _timestamp = InternTimestamp(timestamp);
            FullPath = InternPath(fullPath);
            ReasonString = reasonString;
            FileTime = fileTime;
        }

        // Constructor without pre-formatted timestamp
        public JournalEntry(string fullPath, string reasonString, long fileTime)
        {
            _timestamp = null;
            FullPath = InternPath(fullPath);
            ReasonString = reasonString;
            FileTime = fileTime;
        }

        // String pool for path deduplication
        private static readonly ConcurrentDictionary<string, string> _pathInternPool = new ConcurrentDictionary<string, string>();
        // String pool for timestamp deduplication
        private static readonly ConcurrentDictionary<string, string> _timestampInternPool = new ConcurrentDictionary<string, string>();
        // Cached file extensions to reduce computation
        private static readonly ConcurrentDictionary<string, string> _extensionInternPool = new ConcurrentDictionary<string, string>();
        // Cached lowercase paths for filtering
        private static readonly ConcurrentDictionary<string, string> _lowerPathInternPool = new ConcurrentDictionary<string, string>();
        // String pool for reason deduplication
        private static readonly Dictionary<string, string> _reasonInternPool = new Dictionary<string, string>(StringComparer.Ordinal);

        // Interns path string to reduce memory
        public static string InternPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return _pathInternPool.GetOrAdd(path, path);
        }

        // Interns timestamp string to reduce memory
        public static string InternTimestamp(string timestamp)
        {
            if (string.IsNullOrEmpty(timestamp)) return timestamp;
            return _timestampInternPool.GetOrAdd(timestamp, timestamp);
        }

        // Gets cached file extension in lowercase
        public string GetFileExtension()
        {
            if (string.IsNullOrEmpty(FullPath)) return "";
            
            return _extensionInternPool.GetOrAdd(FullPath, path =>
            {
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) return "";
                return ext.StartsWith(".") ? ext.Substring(1).ToLowerInvariant() : ext.ToLowerInvariant();
            });
        }

        // Gets cached lowercase path for filtering
        public string GetLowerPath()
        {
            if (string.IsNullOrEmpty(FullPath)) return "";
            return _lowerPathInternPool.GetOrAdd(FullPath, path => path.ToLowerInvariant());
        }

        // Converts FileTime to formatted string
        public static string ConvertFileTimeToString(long fileTime)
        {
            try
            {
                var dt = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                var timestampString = dt.ToString("yyyy-MM-dd HH:mm:ss.ff");
                return InternTimestamp(timestampString); // Intern the result
            }
            catch
            {
                return "";
            }
        }

        // Constructs complete path from MFT data
        public static string BuildFullPath(ulong fileFrn, Dictionary<ulong, string> parentPaths, string fileName, string driveLetter)
        {
            if (parentPaths != null && parentPaths.TryGetValue(fileFrn, out var fullPath) && !string.IsNullOrEmpty(fullPath))
                return Path.Combine(fullPath, fileName);
            return (driveLetter ?? "C") + ":\\" + fileName;
        }

        // Recursively constructs complete path using parent FRN mappings
        public static string BuildFullPath(ulong fileFrn, Dictionary<ulong, string> resolvedPaths, Dictionary<ulong, ulong> parentFrnMap, Dictionary<ulong, string> names, string fileName, string driveLetter)
        {
            if (resolvedPaths != null && resolvedPaths.TryGetValue(fileFrn, out var fullPath) && !string.IsNullOrEmpty(fullPath))
            {
                return FixMissingParentPath(Path.Combine(fullPath, fileName), driveLetter);
            }

            if (parentFrnMap != null && names != null)
            {
                var pathStack = new Stack<string>();
                pathStack.Push(fileName);
                ulong currentFrn = fileFrn;
                int maxDepth = 100;
                int depth = 0;

                while (depth < maxDepth && parentFrnMap.TryGetValue(currentFrn, out ulong parentFrn) && parentFrn != currentFrn)
                {
                    if (names.TryGetValue(parentFrn, out var parentName))
                    {
                        pathStack.Push(parentName);
                        currentFrn = parentFrn;
                        depth++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (pathStack.Count > 1)
                {
                    string result = (driveLetter ?? "C") + ":\\" + string.Join("\\", pathStack);
                    return FixMissingParentPath(result, driveLetter);
                }
            }

            return FixMissingParentPath((driveLetter ?? "C") + ":\\" + fileName, driveLetter);
        }

        private static string FixMissingParentPath(string path, string driveLetter)
        {
            if (path.Contains(":\\:"))
            {
                return path.Replace(":\\:", ":\\UNKNOWN_MFT_PARENT\\");
            }
            return path;
        }

        // Converts reason flags to readable text
        public static string GetReasonString(uint reason)
        {
            string result = null;

            if ((reason & 0x00000001) != 0 || (reason & 0x00000010) != 0)
                result = "Overwrite";
            else if ((reason & 0x00000002) != 0 || (reason & 0x00000020) != 0)
                result = "Extended";
            else if ((reason & 0x00000004) != 0 || (reason & 0x00000040) != 0)
                result = "Truncation";
            else if ((reason & 0x00000100) != 0)
                result = "Created";
            else if ((reason & 0x00000200) != 0)
                result = "Deleted";
            else if ((reason & 0x00000400) != 0 || (reason & 0x00000800) != 0 ||
                     (reason & 0x00004000) != 0 || (reason & 0x00010000) != 0 ||
                     (reason & 0x00020000) != 0 || (reason & 0x00040000) != 0 ||
                     (reason & 0x00080000) != 0 || (reason & 0x00100000) != 0 ||
                     (reason & 0x00200000) != 0 || (reason & 0x00400000) != 0)
                result = "DataChange";
            else if ((reason & 0x00008000) != 0)
                result = "Basic";
            else if ((reason & 0x00001000) != 0)
                result = "RenamedFrom";
            else if ((reason & 0x00002000) != 0)
                result = "RenamedTo";
            else if ((reason & 0x80000000) != 0)
                result = "Close";
            else
                result = "ERROR";

            lock (_reasonInternPool)
            {
                if (_reasonInternPool.TryGetValue(result, out var interned))
                    return interned;
                _reasonInternPool[result] = result;
                return result;
            }
        }

        // Clears cached data to free memory
        public static void ClearCaches()
        {
            _extensionInternPool.Clear();
            _lowerPathInternPool.Clear();
        }
    }
}