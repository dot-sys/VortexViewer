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
        // Result containing both resolved paths and parent mappings
        public class MftPathData
        {
            public Dictionary<ulong, string> ResolvedPaths { get; set; }
            public Dictionary<ulong, ulong> ParentFrnMap { get; set; }
            public Dictionary<ulong, string> Names { get; set; }
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

        // Builds path map from MFT data
        public static MftPathData BuildParentPathMap(string driveLetter, HashSet<ulong> neededFrns)
        {
            var result = new MftPathData
            {
                ResolvedPaths = new Dictionary<ulong, string>(),
                ParentFrnMap = new Dictionary<ulong, ulong>(),
                Names = new Dictionary<ulong, string>()
            };
            
            if (neededFrns == null || neededFrns.Count == 0)
                return result;

            var parents = new ConcurrentDictionary<ulong, ulong>();
            var names = new ConcurrentDictionary<ulong, string>();
            string drive = driveLetter.TrimEnd(':', '\\');
            string path = "\\\\.\\" + drive + ":";

            using (var handle = CreateFile(path, 0x80000000, 1 | 2, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    return result;

                var mftEnumData = new MFT_ENUM_DATA
                {
                    StartFileReferenceNumber = 0,
                    LowUsn = 0,
                    HighUsn = long.MaxValue
                };

                int bufferSize = 4 * 1024 * 1024;
                var outBuffer = new byte[bufferSize];
                int bytesReturned = 0;

                while (true)
                {
                    if (!DeviceIoControl(handle, FSCTL_ENUM_USN_DATA, ref mftEnumData, Marshal.SizeOf(typeof(MFT_ENUM_DATA)), outBuffer, bufferSize, out bytesReturned, IntPtr.Zero))
                        break;
                    if (bytesReturned <= 8)
                        break;

                    int offset = 8;
                    while (offset < bytesReturned)
                    {
                        IntPtr recordPtr = Marshal.UnsafeAddrOfPinnedArrayElement(outBuffer, offset);
                        USN_RECORD record = (USN_RECORD)Marshal.PtrToStructure(recordPtr, typeof(USN_RECORD));
                        
                        string fileName = System.Text.Encoding.Unicode.GetString(outBuffer, offset + record.FileNameOffset, record.FileNameLength);
                        names[record.FileReferenceNumber] = fileName;
                        parents[record.FileReferenceNumber] = record.ParentFileReferenceNumber;
                        
                        offset += record.RecordLength;
                    }

                    mftEnumData.StartFileReferenceNumber = BitConverter.ToUInt64(outBuffer, 0);
                }
            }

            var resolved = new Dictionary<ulong, string>();
            foreach (var frn in names.Keys)
            {
                ResolvePath(frn, names, parents, resolved, drive);
            }
            
            // Convert concurrent dictionaries to regular dictionaries
            result.ResolvedPaths = resolved;
            result.ParentFrnMap = new Dictionary<ulong, ulong>(parents);
            result.Names = new Dictionary<ulong, string>(names);
            
            return result;
        }

        // Recursively resolves file path from MFT
        private static string ResolvePath(ulong frn, ConcurrentDictionary<ulong, string> names, ConcurrentDictionary<ulong, ulong> parents, Dictionary<ulong, string> resolved, string drive)
        {
            if (resolved.TryGetValue(frn, out var cached))
                return cached;

            if (!parents.ContainsKey(frn) || !names.ContainsKey(frn))
            {
                resolved[frn] = drive + ":\\";
                return resolved[frn];
            }

            ulong parent = parents[frn];
            if (parent == frn || !names.ContainsKey(parent))
            {
                resolved[frn] = drive + ":\\" + names[frn];
                return resolved[frn];
            }

            var parentPath = ResolvePath(parent, names, parents, resolved, drive);
            resolved[frn] = Path.Combine(parentPath, names[frn]);
            return resolved[frn];
        }
    }
}