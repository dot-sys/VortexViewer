using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

// Core namespace for processing
namespace VortexViewer.Core
{
    // Represents a journal entry
    public readonly struct JournalEntry
    {
        // Timestamp string
        private readonly string _timestamp;
        // Timestamp property
        public string Timestamp => _timestamp ?? ConvertFileTimeToString(FileTime);

        // Path of the file
        public string FullPath { get; }
        // Reason for the entry
        public string ReasonString { get; }
        // File time
        public long FileTime { get; }

        // Constructor without timestamp
        public JournalEntry(string fullPath, string reasonString, long fileTime)
        {
            _timestamp = null;
            FullPath = InternPath(fullPath);
            ReasonString = reasonString;
            FileTime = fileTime;
        }

        // Pool for paths
        private static readonly ConcurrentDictionary<string, string> _pathInternPool = new ConcurrentDictionary<string, string>();
        // Pool for timestamps
        private static readonly ConcurrentDictionary<string, string> _timestampInternPool = new ConcurrentDictionary<string, string>();
        // Pool for extensions
        private static readonly ConcurrentDictionary<string, string> _extensionInternPool = new ConcurrentDictionary<string, string>();
        // Pool for lower paths
        private static readonly ConcurrentDictionary<string, string> _lowerPathInternPool = new ConcurrentDictionary<string, string>();
        // Pool for reasons
        private static readonly Dictionary<string, string> _reasonInternPool = new Dictionary<string, string>(StringComparer.Ordinal);

        // Interns a path
        public static string InternPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return _pathInternPool.GetOrAdd(path, path);
        }

        // Interns a timestamp
        public static string InternTimestamp(string timestamp)
        {
            if (string.IsNullOrEmpty(timestamp)) return timestamp;
            return _timestampInternPool.GetOrAdd(timestamp, timestamp);
        }

        // Gets file extension
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

        // Gets lower path
        public string GetLowerPath()
        {
            if (string.IsNullOrEmpty(FullPath)) return "";
            return _lowerPathInternPool.GetOrAdd(FullPath, path => path.ToLowerInvariant());
        }

        // Converts time to string
        public static string ConvertFileTimeToString(long fileTime)
        {
            try
            {
                var dt = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                var timestampString = dt.ToString("yyyy-MM-dd HH:mm:ss.ff");
                return InternTimestamp(timestampString);
            }
            catch
            {
                return "";
            }
        }

        // Gets reason string
        public static string GetReasonString(uint reason)
        {
            if ((reason & 0x00000100) != 0) 
                return InternReason("Created");

            if ((reason & 0x00000200) != 0) 
                return InternReason("Deleted");

            if ((reason & 0x00001000) != 0) 
                return InternReason("RenameFrom");

            if ((reason & 0x00002000) != 0) 
                return InternReason("RenameTo");

            if ((reason & 0x00000001) != 0) 
                return InternReason("Overwrite");

            if ((reason & 0x00000010) != 0) 
                return InternReason("Overwrite");

            if ((reason & 0x00000002) != 0) 
                return InternReason("Extended");

            if ((reason & 0x00000020) != 0) 
                return InternReason("Extended");

            if ((reason & 0x00000004) != 0) 
                return InternReason("Truncation");

            if ((reason & 0x00000040) != 0) 
                return InternReason("Truncation");

            if ((reason & 0x00000400) != 0) 
                return InternReason("EAChange");

            if ((reason & 0x00000800) != 0) 
                return InternReason("SecurityChange");

            if ((reason & 0x00004000) != 0) 
                return InternReason("IndexableChange");

            if ((reason & 0x00008000) != 0) 
                return InternReason("BasicInfoChange");

            if ((reason & 0x00010000) != 0) 
                return InternReason("HardLinkChange");

            if ((reason & 0x00020000) != 0) 
                return InternReason("CompressionChange");

            if ((reason & 0x00040000) != 0) 
                return InternReason("EncryptionChange");

            if ((reason & 0x00080000) != 0) 
                return InternReason("ObjectIdChange");

            if ((reason & 0x00100000) != 0) 
                return InternReason("ReparsePointChange");

            if ((reason & 0x00200000) != 0) 
                return InternReason("StreamChange");

            if ((reason & 0x00400000) != 0) 
                return InternReason("TransactedChange");

            if ((reason & 0x80000000) != 0) 
                return InternReason("Close");

            return InternReason("Unknown");
        }

        // Interns a reason
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

        // Clears the caches
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