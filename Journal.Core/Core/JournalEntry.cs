using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

// Core journal processing and data types
namespace VortexViewer.Core
{
    // High-level journal entry with formatted data
    public readonly struct JournalEntry
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
        // ENHANCED: Now attempts on-the-fly reconstruction when parent path missing from cache
        public static string BuildFullPath(ulong fileFrn, Dictionary<ulong, string> resolvedPaths, Dictionary<ulong, ulong> parentFrnMap, Dictionary<ulong, string> names, string fileName, string driveLetter)
        {
            string drive = (driveLetter ?? "C").TrimEnd(':', '\\');
            string rootPrefix = drive + @":\";
            
            string finalPath;
            
            // 1) If we know the parent FRN and have its absolute path from resolvedPaths -> append fileName.
            if (parentFrnMap != null && parentFrnMap.TryGetValue(fileFrn, out ulong parentFrn))
            {
                if (resolvedPaths != null && resolvedPaths.TryGetValue(parentFrn, out var parentPath) && !string.IsNullOrEmpty(parentPath))
                {
                    // parentPath is expected to be an absolute directory path like "C:\Windows\System32" or "C:\UNKNOWN_MFT_PARENT"
                    if (!parentPath.Contains("UNKNOWN_MFT_PARENT"))
                    {
                        // Normal, fully resolved case
                        if (parentPath.EndsWith("\\"))
                            finalPath = parentPath + fileName;
                        else
                            finalPath = parentPath + "\\" + fileName;
                    }
                    else
                    {
                        // ParentPath is UNKNOWN_MFT_PARENT variant, but still absolute.
                        if (parentPath.EndsWith("\\"))
                            finalPath = parentPath + fileName;
                        else
                            finalPath = parentPath + "\\" + fileName;
                    }
                }
                else
                {
                    // FALLBACK: Parent path not in cache - try to reconstruct on-the-fly
                    // This handles cases where parent exists in MFT but wasn't pre-resolved
                    string reconstructedParentPath = ReconstructParentPath(parentFrn, parentFrnMap, names, drive, resolvedPaths);
                    
                    if (!string.IsNullOrEmpty(reconstructedParentPath) && !reconstructedParentPath.Contains("UNKNOWN_MFT_PARENT"))
                    {
                        // Successfully reconstructed - cache it for future use
                        if (resolvedPaths != null)
                        {
                            resolvedPaths[parentFrn] = reconstructedParentPath;
                        }
                        
                        if (reconstructedParentPath.EndsWith("\\"))
                            finalPath = reconstructedParentPath + fileName;
                        else
                            finalPath = reconstructedParentPath + "\\" + fileName;
                    }
                    else
                    {
                        // Reconstruction failed → last-resort unknown parent path
                        finalPath = rootPrefix + @"UNKNOWN_MFT_PARENT\" + fileName;
                    }
                }
            }
            else
            {
                // 2) No parentFrn entry at all: absolute last resort – just put it at drive root.
                finalPath = rootPrefix + fileName;
            }
            
            return finalPath;
        }

        // Helper method to reconstruct parent path on-the-fly when missing from cache
        // Walks parent chain up to root or until hitting a cached path
        private static string ReconstructParentPath(
            ulong parentFrn, 
            Dictionary<ulong, ulong> parentFrnMap, 
            Dictionary<ulong, string> names, 
            string drive,
            Dictionary<ulong, string> resolvedPathsCache,
            int depth = 0)
        {
            // Prevent infinite recursion
            if (depth > 256)
                return null;
            
            // Root directory (FRN 5)
            if (parentFrn == 5 || parentFrn == 0)
                return drive + @":\";
            
            // Check if already in cache
            if (resolvedPathsCache != null && resolvedPathsCache.TryGetValue(parentFrn, out var cachedPath))
                return cachedPath;
            
            // Check if this FRN has parent and name information
            if (parentFrnMap == null || !parentFrnMap.TryGetValue(parentFrn, out ulong grandparentFrn))
                return null; // Can't reconstruct - no parent info
            
            if (names == null || !names.TryGetValue(parentFrn, out string currentName))
                return null; // Can't reconstruct - no name info
            
            // Check if parent is root
            if (grandparentFrn == 5 || grandparentFrn == 0)
            {
                string path = drive + @":\" + currentName;
                // Cache it
                if (resolvedPathsCache != null)
                    resolvedPathsCache[parentFrn] = path;
                return path;
            }
            
            // Recursively get grandparent path
            string grandparentPath = ReconstructParentPath(grandparentFrn, parentFrnMap, names, drive, resolvedPathsCache, depth + 1);
            
            if (string.IsNullOrEmpty(grandparentPath) || grandparentPath.Contains("UNKNOWN_MFT_PARENT"))
                return null; // Failed to reconstruct parent chain
            
            // Build full path
            string fullPath;
            if (grandparentPath.EndsWith("\\"))
                fullPath = grandparentPath + currentName;
            else
                fullPath = grandparentPath + "\\" + currentName;
            
            // Cache it
            if (resolvedPathsCache != null)
                resolvedPathsCache[parentFrn] = fullPath;
            
            return fullPath;
        }

        // Converts reason flags to readable text
        public static string GetReasonString(uint reason)
        {
            // USN Reason flags according to Microsoft documentation
            // https://docs.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-usn_record_v2
            
            // Return the MOST SIGNIFICANT reason (prioritized by importance)
            // Check in order of priority: Create/Delete/Rename first, then modifications
            
            if ((reason & 0x00000100) != 0) // USN_REASON_FILE_CREATE
                return InternReason("Created");
            
            if ((reason & 0x00000200) != 0) // USN_REASON_FILE_DELETE
                return InternReason("Deleted");
            
            // SEPARATE RENAME FLAGS: Old name vs New name
            if ((reason & 0x00001000) != 0) // USN_REASON_RENAME_OLD_NAME
                return InternReason("RenameFrom");
            
            if ((reason & 0x00002000) != 0) // USN_REASON_RENAME_NEW_NAME
                return InternReason("RenameTo");
            
            if ((reason & 0x00000001) != 0) // USN_REASON_DATA_OVERWRITE
                return InternReason("Overwrite");
            
            if ((reason & 0x00000010) != 0) // USN_REASON_NAMED_DATA_OVERWRITE
                return InternReason("Overwrite");
            
            if ((reason & 0x00000002) != 0) // USN_REASON_DATA_EXTEND
                return InternReason("Extended");
            
            if ((reason & 0x00000020) != 0) // USN_REASON_NAMED_DATA_EXTEND
                return InternReason("Extended");
            
            if ((reason & 0x00000004) != 0) // USN_REASON_DATA_TRUNCATION
                return InternReason("Truncation");
            
            if ((reason & 0x00000040) != 0) // USN_REASON_NAMED_DATA_TRUNCATION
                return InternReason("Truncation");
            
            if ((reason & 0x00000400) != 0) // USN_REASON_EA_CHANGE
                return InternReason("EAChange");
            
            if ((reason & 0x00000800) != 0) // USN_REASON_SECURITY_CHANGE
                return InternReason("SecurityChange");
            
            if ((reason & 0x00004000) != 0) // USN_REASON_INDEXABLE_CHANGE
                return InternReason("IndexableChange");
            
            if ((reason & 0x00008000) != 0) // USN_REASON_BASIC_INFO_CHANGE
                return InternReason("BasicInfoChange");
            
            if ((reason & 0x00010000) != 0) // USN_REASON_HARD_LINK_CHANGE
                return InternReason("HardLinkChange");
            
            if ((reason & 0x00020000) != 0) // USN_REASON_COMPRESSION_CHANGE
                return InternReason("CompressionChange");
            
            if ((reason & 0x00040000) != 0) // USN_REASON_ENCRYPTION_CHANGE
                return InternReason("EncryptionChange");
            
            if ((reason & 0x00080000) != 0) // USN_REASON_OBJECT_ID_CHANGE
                return InternReason("ObjectIdChange");
            
            if ((reason & 0x00100000) != 0) // USN_REASON_REPARSE_POINT_CHANGE
                return InternReason("ReparsePointChange");
            
            if ((reason & 0x00200000) != 0) // USN_REASON_STREAM_CHANGE
                return InternReason("StreamChange");
            
            if ((reason & 0x00400000) != 0) // USN_REASON_TRANSACTED_CHANGE
                return InternReason("TransactedChange");
            
            if ((reason & 0x80000000) != 0) // USN_REASON_CLOSE
                return InternReason("Close");
            
            // Should never reach here with valid USN data
            return InternReason("Unknown");
        }

        // Helper method to intern reason strings
        private static string InternReason(string reason)
        {
            lock (_reasonInternPool)
            {
                if (_reasonInternPool.TryGetValue(reason, out var interned))
                    return interned;
                _reasonInternPool[reason] = reason;
                return reason;
            }
        }

        // Clears all internal caches
        public static void ClearCaches()
        {
            _pathInternPool.Clear();
            _timestampInternPool.Clear();
            _extensionInternPool.Clear();
            _lowerPathInternPool.Clear();
            lock (_reasonInternPool)
            {
                _reasonInternPool.Clear();
            }
        }
    }
}