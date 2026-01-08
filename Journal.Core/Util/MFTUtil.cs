using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// Utility classes for journal operations
namespace VortexViewer.Journal.Core.Util
{
    // MFT parser for building file paths
    public static class MftUtil
    {
        // MFT record data
        public struct MftRecord
        {
            public ulong ParentFrn;
            public string Name;
            public uint Attributes;
        }

        // Result containing both resolved paths and parent mappings
        public class MftPathData
        {
            public Dictionary<ulong, string> ResolvedPaths { get; set; }
            public Dictionary<ulong, ulong> ParentFrnMap { get; set; }
            public Dictionary<ulong, string> Names { get; set; }
            public PathResolutionStats Stats { get; set; }
        }
        
        // Statistics for path resolution diagnostics
        public class PathResolutionStats
        {
            public int TotalRequested { get; set; }
            public int FullyResolved { get; set; }
            public int FallbackMatches { get; set; }
            public int TrueOrphans { get; set; }
            public int DeletedFiles { get; set; }
        }

        // IOCTL code for MFT enumeration
        private const uint FSCTL_ENUM_USN_DATA = 0x000900b3;

        // Native structure for MFT enumeration
        [StructLayout(LayoutKind.Sequential)]
        private struct MFT_ENUM_DATA
        {
            public ulong StartFileReferenceNumber;
            public long LowUsn;
            public long HighUsn;
        }

        // Native structure for USN record
        [StructLayout(LayoutKind.Sequential)]
        private struct USN_RECORD
        {
            public int RecordLength;
            public short MajorVersion;
            public short MinorVersion;
            public ulong FileReferenceNumber;
            public ulong ParentFileReferenceNumber;
            public long Usn;
            public long TimeStamp;
            public uint Reason;
            public uint SourceInfo;
            public uint SecurityId;
            public uint FileAttributes;
            public short FileNameLength;
            public short FileNameOffset;
        }

        // Native API for file handle creation
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        // Native API for device control operations
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            ref MFT_ENUM_DATA inBuffer, int nInBufferSize,
            [Out] byte[] outBuffer, int nOutBufferSize,
            out int bytesReturned, IntPtr overlapped);

        // Helper method for safe structure reading
        private static bool TryReadStructure<T>(byte[] buffer, int offset, out T result) where T : struct
        {
            result = default;
            int size = Marshal.SizeOf(typeof(T));
            if (offset < 0 || offset + size > buffer.Length)
                return false;
            
            IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, offset);
            result = (T)Marshal.PtrToStructure(ptr, typeof(T));
            return true;
        }

        // PHASE 1: Enumerate the COMPLETE MFT (no early exit based on FRN matching)
        // Returns complete mapping of ALL files/directories on the volume
        private static Dictionary<ulong, MftRecord> EnumerateCompleteMFT(string driveLetter)
        {
            var completeMftMap = new Dictionary<ulong, MftRecord>
            {
                // Add root directory (FRN 5) as a known entity
                [5] = new MftRecord { ParentFrn = 5, Name = "", Attributes = 0x10 }
            };
            
            string drive = driveLetter.TrimEnd(':', '\\');
            string path = "\\\\.\\" + drive + ":";

            using (var handle = CreateFile(path, 0x80000000, 1 | 2, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    return completeMftMap;
                }

                var mftEnumData = new MFT_ENUM_DATA
                {
                    StartFileReferenceNumber = 0,
                    LowUsn = 0,
                    HighUsn = long.MaxValue
                };

                int bufferSize = 4 * 1024 * 1024; // 4MB buffer
                var outBuffer = new byte[bufferSize];
                const int minRecordSize = 60;
                int iteration = 0;
                int totalRecordsProcessed = 0;
                ulong lastSeenNextFrn = 0;

                while (true)
                {
                    iteration++;
                    
                    // Call DeviceIoControl to enumerate MFT records
                    if (!DeviceIoControl(handle, FSCTL_ENUM_USN_DATA, ref mftEnumData, Marshal.SizeOf(typeof(MFT_ENUM_DATA)), outBuffer, bufferSize, out int bytesReturned, IntPtr.Zero))
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        
                        // Error 38 (ERROR_HANDLE_EOF) or 1168 (ERROR_NOT_FOUND) indicates end of MFT
                        if (errorCode == 38 || errorCode == 1168)
                        {
                            // End of MFT reached
                        }
                        break;
                    }
                    
                    // Check if we got meaningful data
                    if (bytesReturned <= 8)
                    {
                        break;
                    }

                    // Extract the next start FRN from the first 8 bytes
                    ulong nextStartFrn = BitConverter.ToUInt64(outBuffer, 0);
                    
                    // Prevent infinite loops with a safety limit
                    if (iteration > 1000000)
                    {
                        break;
                    }

                    // Parse all USN records in the buffer
                    int offset = 8; // Skip the first 8 bytes (NextStartFrn)
                    int recordsInBatch = 0;
                    
                    while (offset < bytesReturned)
                    {
                        // Ensure we have enough data for a complete record header
                        if (offset + minRecordSize > bytesReturned)
                            break;
                        
                        // Use safe structure reading
                        if (!TryReadStructure<USN_RECORD>(outBuffer, offset, out var record))
                        {
                            break;
                        }
                        
                        // Validate record length (sanity check)
                        if (record.RecordLength < minRecordSize || record.RecordLength > 10000)
                        {
                            break;
                        }
                        
                        if (offset + record.RecordLength > bytesReturned)
                            break;
                        
                        recordsInBatch++;
                        totalRecordsProcessed++;
                        
                        // Extract filename from the record
                        string fileName = "";
                        try
                        {
                            if (record.FileNameLength > 0 && record.FileNameLength < 1024)
                            {
                                fileName = System.Text.Encoding.Unicode.GetString(outBuffer, offset + record.FileNameOffset, record.FileNameLength);
                            }
                        }
                        catch (Exception)
                        {
                            // Failed to extract filename
                        }
                        
                        // Add to complete MFT map (overwrite if duplicate - keep latest)
                        completeMftMap[record.FileReferenceNumber] = new MftRecord
                        {
                            ParentFrn = record.ParentFileReferenceNumber,
                            Name = fileName,
                            Attributes = record.FileAttributes
                        };
                        
                        offset += record.RecordLength;
                    }
                    
                    // STALLED ENUMERATION DETECTION: Check for lack of progress
                    // This handles external SSDs that return data but stop advancing
                    if (nextStartFrn == lastSeenNextFrn)
                    {
                        break;
                    }
                    
                    if (bytesReturned == 8)
                    {
                        break;
                    }
                    
                    if (recordsInBatch == 0)
                    {
                        break;
                    }
                    
                    // Update tracking for next iteration
                    lastSeenNextFrn = nextStartFrn;
                    
                    // Update StartFileReferenceNumber for next iteration
                    mftEnumData.StartFileReferenceNumber = nextStartFrn;
                }
            }

            return completeMftMap;
        }

        // PHASE 2: Build paths from the complete MFT map
        // NOW BUILDS PATHS FOR **ALL** MFT RECORDS, NOT JUST JOURNAL-REFERENCED ONES
        private static Dictionary<ulong, string> BuildPathsFromCompleteMFT(
            Dictionary<ulong, MftRecord> completeMftMap, 
            string driveLetter, 
            Dictionary<ulong, ulong> journalParentMap,
            out PathResolutionStats stats)
        {
            stats = new PathResolutionStats();
            var resolvedPaths = new Dictionary<ulong, string>();
            string drive = driveLetter.TrimEnd(':', '\\');
            
            // Initialize root
            resolvedPaths[5] = drive + @":\";
            
            // CRITICAL CHANGE: Resolve paths for ALL MFT records, not just journalParentMap FRNs
            // This ensures parent directories are pre-cached before journal entries need them
            var allFrns = new HashSet<ulong>(completeMftMap.Keys);
            
            // Add journal-referenced FRNs to ensure they're prioritized
            if (journalParentMap != null)
            {
                foreach (var frn in journalParentMap.Keys)
                {
                    allFrns.Add(frn);
                }
            }
            
            stats.TotalRequested = allFrns.Count;
            
            // Phase 2a: First pass - resolve all direct children of root (fast path)
            int directRootChildren = 0;
            foreach (var frn in allFrns)
            {
                if (frn == 5 || frn == 0)
                    continue;
                
                if (completeMftMap.TryGetValue(frn, out var record))
                {
                    if (record.ParentFrn == 5 || record.ParentFrn == 0)
                    {
                        string path = drive + @":\" + record.Name;
                        resolvedPaths[frn] = path;
                        directRootChildren++;
                    }
                }
            }
            
            // Phase 2b: Iteratively resolve remaining FRNs by walking parent chains
            int maxIterations = 100; // Safety limit to prevent infinite loops
            int iteration = 0;
            int _ = resolvedPaths.Count;
            
            while (iteration < maxIterations)
            {
                iteration++;
                int resolvedInThisIteration = 0;
                
                foreach (var frn in allFrns)
                {
                    // Skip if already resolved
                    if (resolvedPaths.ContainsKey(frn))
                        continue;
                    
                    // Skip root
                    if (frn == 5 || frn == 0)
                        continue;
                    
                    // Get parent FRN hint from journal if available
                    ulong parentEntryNumberHint = 0;
                    if (journalParentMap != null && journalParentMap.TryGetValue(frn, out ulong journalParentFrn))
                    {
                        parentEntryNumberHint = journalParentFrn & 0x0000FFFFFFFFFFFF;
                    }
                    
                    string path = ResolvePathFromCompleteMFT(frn, completeMftMap, resolvedPaths, drive, parentEntryNumberHint, stats);
                    
                    if (!string.IsNullOrEmpty(path) && !path.Contains("UNKNOWN_MFT_PARENT"))
                    {
                        resolvedPaths[frn] = path;
                        resolvedInThisIteration++;
                        stats.FullyResolved++;
                    }
                }
                
                // Stop if no progress was made
                if (resolvedInThisIteration == 0)
                {
                    break;
                }
            }
            
            // Calculate final statistics
            stats.TrueOrphans = allFrns.Count - resolvedPaths.Count;
            stats.DeletedFiles = allFrns.Count - completeMftMap.Count; // FRNs not in MFT
            
            return resolvedPaths;
        }

        // Recursively resolve path from complete MFT map with caching and fallback matching
        // ENHANCED: Now caches intermediate paths more aggressively
        private static string ResolvePathFromCompleteMFT(
            ulong frn, 
            Dictionary<ulong, MftRecord> completeMftMap, 
            Dictionary<ulong, string> resolvedCache, 
            string drive, 
            ulong parentEntryNumberHint,
            PathResolutionStats stats,
            int depth = 0)
        {
            // Define consistent root prefix once
            string rootPrefix = drive + @":\";
            
            // Prevent infinite recursion
            if (depth > 256)
            {
                return rootPrefix + "UNKNOWN_MFT_PARENT";
            }

            // Root directory
            if (frn == 5 || frn == 0)
            {
                if (!resolvedCache.ContainsKey(frn))
                    resolvedCache[frn] = rootPrefix;
                return resolvedCache[frn];
            }

            // Check cache (now contains all successfully resolved paths)
            if (resolvedCache.TryGetValue(frn, out var cached))
                return cached;

            // Check if FRN exists in complete MFT map
            if (!completeMftMap.TryGetValue(frn, out var record))
            {
                // FRN not found in MFT - file may have been deleted or journal entry is stale
                return rootPrefix + "UNKNOWN_MFT_PARENT";
            }

            // Check for root parent
            if (record.ParentFrn == 5 || record.ParentFrn == 0 || record.ParentFrn == frn)
            {
                // Direct child of root
                string directPath = rootPrefix + record.Name;
                resolvedCache[frn] = directPath;
                return directPath;
            }

            // ENHANCED PARENT RESOLUTION LOGIC
            ulong resolvedParentFrn = record.ParentFrn;
            bool parentResolved = false;
            
            // Strategy 1: Check if parent is already in resolved cache (fastest)
            if (resolvedCache.ContainsKey(record.ParentFrn))
            {
                resolvedParentFrn = record.ParentFrn;
                parentResolved = true;
            }
            // Strategy 2: Check if parent exists in MFT with exact FRN match
            else if (completeMftMap.ContainsKey(record.ParentFrn))
            {
                resolvedParentFrn = record.ParentFrn;
                parentResolved = true;
            }
            // Strategy 3: FALLBACK - Try entry number matching (ignore sequence number)
            else if (parentEntryNumberHint != 0 || record.ParentFrn != 0)
            {
                ulong parentEntryNumber = record.ParentFrn & 0x0000FFFFFFFFFFFF;
                
                // Try to find any MFT record where entry index matches (ignoring sequence number)
                foreach (var kvp in completeMftMap)
                {
                    ulong candidateEntryNumber = kvp.Key & 0x0000FFFFFFFFFFFF;
                    if (candidateEntryNumber == parentEntryNumber)
                    {
                        // Found a match by entry number (sequence may differ due to file reuse)
                        resolvedParentFrn = kvp.Key;
                        parentResolved = true;
                        
                        // Only increment stat at top level (depth == 0)
                        if (depth == 0)
                        {
                            stats.FallbackMatches++;
                        }
                        break;
                    }
                }
            }
            
            // If parent still can't be resolved
            if (!parentResolved)
            {
                // Don't cache incomplete paths - return directly
                return rootPrefix + @"UNKNOWN_MFT_PARENT\" + record.Name;
            }

            // Recursively resolve parent path
            var parentPath = ResolvePathFromCompleteMFT(resolvedParentFrn, completeMftMap, resolvedCache, drive, 0, stats, depth + 1);
            
            // If parent resolution failed, propagate the failure
            if (parentPath.Contains("UNKNOWN_MFT_PARENT"))
            {
                // Don't cache incomplete paths
                return parentPath + @"\" + record.Name;
            }
            
            // Build complete path
            string fullPath;
            if (parentPath.EndsWith("\\"))
                fullPath = parentPath + record.Name;
            else
                fullPath = parentPath + @"\" + record.Name;
            
            // CRITICAL FIX: Cache ALL successfully resolved paths (parent was valid)
            resolvedCache[frn] = fullPath;
            
            return fullPath;
        }

        // PUBLIC API: Builds path map from MFT data (maintains backward compatibility)
        // Now uses two-phase approach: complete MFT enumeration + path resolution
        public static MftPathData BuildParentPathMap(string driveLetter, Dictionary<ulong, ulong> journalParentMap)
        {
            var result = new MftPathData
            {
                ResolvedPaths = new Dictionary<ulong, string>(),
                ParentFrnMap = new Dictionary<ulong, ulong>(),
                Names = new Dictionary<ulong, string>(),
                Stats = new PathResolutionStats()
            };
            
            if (journalParentMap == null || journalParentMap.Count == 0)
            {
                return result;
            }

            // PHASE 1: Enumerate complete MFT (no filtering, scan everything)
            var completeMftMap = EnumerateCompleteMFT(driveLetter);
            
            if (completeMftMap.Count == 0)
            {
                return result;
            }

            // PHASE 2: Build paths for needed FRNs from complete map
            var resolvedPaths = BuildPathsFromCompleteMFT(completeMftMap, driveLetter, journalParentMap, out var stats);
            result.Stats = stats;

            // Convert completeMftMap to legacy format for backward compatibility
            foreach (var kvp in completeMftMap)
            {
                result.ParentFrnMap[kvp.Key] = kvp.Value.ParentFrn;
                result.Names[kvp.Key] = kvp.Value.Name;
            }
            
            result.ResolvedPaths = resolvedPaths;
            
            return result;
        }
    }
}