using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VortexViewer.Journal.Core.Models;

// NTFS Master File Table utilities
namespace VortexViewer.Journal.Core.Util
{
    // Extracts file metadata from MFT
    public static class MftUtil
    {
        // IOCTL code for enumeration
        private const uint FSCTL_ENUM_USN_DATA = 0x000900b3;

        // Native MFT enumeration data
        [StructLayout(LayoutKind.Sequential)]
        private struct MFT_ENUM_DATA
        {
            // Start file reference number
            public ulong StartFileReferenceNumber;
            // Low USN
            public long LowUsn;
            // High USN
            public long HighUsn;
        }

        // Native USN record
        [StructLayout(LayoutKind.Sequential)]
        private struct USN_RECORD
        {
            // Length of record
            public int RecordLength;
            // Major version
            public short MajorVersion;
            // Minor version
            public short MinorVersion;
            // Reference number of file
            public ulong FileReferenceNumber;
            // Reference number of parent
            public ulong ParentFileReferenceNumber;
            // Update sequence number
            public long Usn;
            // Time stamp
            public long TimeStamp;
            // Reason
            public uint Reason;
            // Source information
            public uint SourceInfo;
            // Security ID
            public uint SecurityId;
            // Attributes of file
            public uint FileAttributes;
            // Length of file name
            public short FileNameLength;
            // Offset of file name
            public short FileNameOffset;
        }

        // Native API CreateFile
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        // Native API DeviceIoControl
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            ref MFT_ENUM_DATA inBuffer, int nInBufferSize,
            [Out] byte[] outBuffer, int nOutBufferSize,
            out int bytesReturned, IntPtr overlapped);

        // Reads a structure safely
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

        // Gets the initial state map
        public static Dictionary<ulong, HistoricalState> GetInitialStateMap(string driveLetter)
        {
            const ulong ROOT_ENTRY = 5;

            var stateMap = new Dictionary<ulong, HistoricalState>
            {
                [ROOT_ENTRY] = new HistoricalState { ParentFRN = ROOT_ENTRY, Name = "", IsDirectory = true }
            };

            string drive = driveLetter.TrimEnd(':', '\\');
            string path = "\\\\.\\" + drive + ":";

            using (var handle = CreateFile(path, 0x80000000, 1 | 2, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    return stateMap;

                var mftEnumData = new MFT_ENUM_DATA
                {
                    StartFileReferenceNumber = 0,
                    LowUsn = 0,
                    HighUsn = long.MaxValue
                };

                int bufferSize = 4 * 1024 * 1024; 
                var outBuffer = new byte[bufferSize];
                const int minRecordSize = 60;
                ulong lastSeenNextFrn = ulong.MaxValue;
                int iteration = 0;

                while (iteration < 1_000_000)
                {
                    iteration++;

                    if (!DeviceIoControl(handle, FSCTL_ENUM_USN_DATA, ref mftEnumData,
                        Marshal.SizeOf(typeof(MFT_ENUM_DATA)), outBuffer, bufferSize,
                        out int bytesReturned, IntPtr.Zero))
                    {
                        break;
                    }

                    if (bytesReturned <= 8)
                        break;

                    ulong nextStartFrn = BitConverter.ToUInt64(outBuffer, 0);

                    if (nextStartFrn == lastSeenNextFrn)
                        break;

                    int offset = 8;
                    int recordsInBatch = 0;

                    while (offset < bytesReturned)
                    {
                        if (offset + minRecordSize > bytesReturned)
                            break;

                        if (!TryReadStructure<USN_RECORD>(outBuffer, offset, out var record))
                            break;

                        if (record.RecordLength < minRecordSize || record.RecordLength > 65536)
                            break;

                        if (offset + record.RecordLength > bytesReturned)
                            break;

                        recordsInBatch++;

                        string fileName = string.Empty;
                        try
                        {
                            if (record.FileNameLength > 0 && record.FileNameLength < 1024)
                                fileName = System.Text.Encoding.Unicode.GetString(
                                    outBuffer, offset + record.FileNameOffset, record.FileNameLength);
                        }
                        catch { }

                        bool isDirectory = (record.FileAttributes & 0x10) != 0;

                        ulong fileEntry   = record.FileReferenceNumber       & 0x0000FFFFFFFFFFFF;
                        ulong parentEntry = record.ParentFileReferenceNumber & 0x0000FFFFFFFFFFFF;

                        stateMap[fileEntry] = new HistoricalState
                        {
                            ParentFRN   = parentEntry,
                            Name        = fileName,
                            IsDirectory = isDirectory
                        };

                        offset += record.RecordLength;
                    }

                    if (recordsInBatch == 0)
                        break;

                    lastSeenNextFrn = nextStartFrn;
                    mftEnumData.StartFileReferenceNumber = nextStartFrn;
                }
            }

            return stateMap;
        }
    }
}