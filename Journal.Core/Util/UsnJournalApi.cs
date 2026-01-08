using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using VortexViewer.Journal.Core.Models;

namespace VortexViewer.Journal.Core.Util
{
    public static class UsnJournalApi
    {
        private const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;
        private const uint FSCTL_READ_USN_JOURNAL = 0x000900bb;

        [StructLayout(LayoutKind.Sequential)]
        private struct USN_JOURNAL_DATA
        {
            public ulong UsnJournalID;
            public ulong FirstUsn;
            public ulong NextUsn;
            public ulong LowestValidUsn;
            public ulong MaxUsn;
            public ulong MaximumSize;
            public ulong AllocationDelta;
            public ushort MinSupportedMajorVersion;
            public ushort MaxSupportedMajorVersion;
            public uint Flags;
            public ulong RangeTrackChunkSize;
            public long RangeTrackFileSizeThreshold;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct READ_USN_JOURNAL_DATA
        {
            public long StartUsn;
            public uint ReasonMask;
            public uint ReturnOnlyOnClose;
            public ulong Timeout;
            public ulong BytesToWaitFor;
            public ulong UsnJournalID;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        // DeviceIoControl overload for QUERY operations (no input buffer)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            [Out] byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        // DeviceIoControl overload for READ operations (with structured input)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref READ_USN_JOURNAL_DATA lpInBuffer,
            int nInBufferSize,
            [Out] byte[] lpOutBuffer,
            int nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        public static List<string> GetNTFSDivides()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType != DriveType.CDRom && d.DriveFormat == "NTFS")
                .Select(d => d.Name)
                .ToList();
        }

        public static UsnJournalInfo QueryJournal(string driveLetter)
        {
            var drive = driveLetter.TrimEnd('\\');
            var path = $"\\\\.\\{drive}";
            using (var handle = CreateFile(
                path,
                0x80000000, // GENERIC_READ
                1 | 2,      // FILE_SHARE_READ | FILE_SHARE_WRITE
                IntPtr.Zero,
                3,          // OPEN_EXISTING
                0,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    return null; // No journal or cannot open

                var outBuffer = new byte[Marshal.SizeOf(typeof(USN_JOURNAL_DATA))];
                if (!DeviceIoControl(
                    handle,
                    FSCTL_QUERY_USN_JOURNAL,
                    IntPtr.Zero,
                    0,
                    outBuffer,
                    (uint)outBuffer.Length,
                    out uint bytesReturned,
                    IntPtr.Zero))
                    return null; // No journal

                // Allocate unmanaged memory and copy the data to avoid RCW race condition
                IntPtr ptr = Marshal.AllocHGlobal(outBuffer.Length);
                try
                {
                    Marshal.Copy(outBuffer, 0, ptr, outBuffer.Length);
                    var data = (USN_JOURNAL_DATA)Marshal.PtrToStructure(ptr, typeof(USN_JOURNAL_DATA));
                    return new UsnJournalInfo
                    {
                        DriveLetter = driveLetter,
                        JournalId = data.UsnJournalID,
                        MaximumSize = data.MaximumSize,
                        AllocationDelta = data.AllocationDelta,
                        FirstUsn = data.FirstUsn,
                        NextUsn = data.NextUsn
                    };
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }

        public static List<UsnJournalEntry> ReadJournalEntries(string driveLetter)
        {
            var entries = new List<UsnJournalEntry>(3_000_000);
            var drive = driveLetter.TrimEnd('\\');
            var path = $"\\\\.\\{drive}";
            
            using (var handle = CreateFile(
                path,
                0x80000000, // GENERIC_READ only (removed GENERIC_WRITE to avoid Defender blocks)
                1 | 2,      // FILE_SHARE_READ | FILE_SHARE_WRITE
                IntPtr.Zero,
                3,          // OPEN_EXISTING
                0,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    return entries;
                }

                var journalInfo = QueryJournal(driveLetter);
                if (journalInfo == null)
                {
                    return entries;
                }

                long startUsn = (long)journalInfo.FirstUsn;
                const int bufferSize = 4 * 1024 * 1024;
                const int minRecordSize = 60; // Minimum USN_RECORD_V2 size
                var outBuffer = new byte[bufferSize];
                int maxIterations = 10000; // Safety limit to prevent infinite loops
                int iteration = 0;

                while (iteration < maxIterations)
                {
                    iteration++;
                    
                    var inData = new READ_USN_JOURNAL_DATA
                    {
                        StartUsn = startUsn,
                        ReasonMask = 0xFFFFFFFF,
                        ReturnOnlyOnClose = 0,
                        Timeout = 0,
                        BytesToWaitFor = 0,
                        UsnJournalID = journalInfo.JournalId
                    };

                    // Use direct structure passing instead of manual allocation
                    if (!DeviceIoControl(
                        handle,
                        FSCTL_READ_USN_JOURNAL,
                        ref inData,
                        Marshal.SizeOf(typeof(READ_USN_JOURNAL_DATA)),
                        outBuffer,
                        outBuffer.Length,
                        out uint bytesReturned,
                        IntPtr.Zero))
                    {
                        break;
                    }

                    if (bytesReturned < 8)
                    {
                        break;
                    }

                    long lastUsn = BitConverter.ToInt64(outBuffer, 0);
                    
                    // If we got the same USN or went backwards, we're done
                    if (lastUsn == startUsn)
                    {
                        break;
                    }

                    int offset = 8;
                    int recordsParsed = 0;
                    
                    while (offset < bytesReturned)
                    {
                        // Need at least 4 bytes for record length
                        if (offset + 4 > bytesReturned)
                            break;

                        int recordLength = BitConverter.ToInt32(outBuffer, offset);
                        
                        // Validate record length
                        if (recordLength < minRecordSize || 
                            recordLength > bufferSize || 
                            offset + recordLength > bytesReturned)
                            break;

                        var record = UsnJournalEntry.Parse(outBuffer, offset);
                        entries.Add(record);
                        recordsParsed++;
                        
                        // USN records are 8-byte aligned
                        int alignedLength = (recordLength + 7) & ~7;
                        offset += alignedLength;
                    }

                    // If we didn't parse any records but got data, something is wrong
                    if (recordsParsed == 0 && bytesReturned > 8)
                    {
                        break;
                    }

                    // Move to the next USN position
                    startUsn = lastUsn;
                }
            }
            
            return entries;
        }

        public static List<string> GetDrivesWithUsnJournal()
        {
            var ntfsDrives = GetNTFSDivides();
            var drivesWithUsn = new List<string>();
            foreach (var drive in ntfsDrives)
            {
                if (QueryJournal(drive) != null)
                    drivesWithUsn.Add(drive);
            }
            return drivesWithUsn;
        }
    }
}