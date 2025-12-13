using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// Process memory analysis utilities
namespace Processes.Core.Util
{
    /// Contains extracted strings and region info
    public class ProcessStringsResult
    {
        /// All extracted strings from memory
        public List<string> Strings { get; set; }
        /// Scanned memory regions metadata
        public List<MemoryRegionInfo> Regions { get; set; }
    }

    /// Memory region address and protection info
    public class MemoryRegionInfo
    {
        /// Starting address of memory region
        public ulong BaseAddress { get; set; }
        /// Total size of region bytes
        public ulong Size { get; set; }
        /// Memory protection flags hex string
        public string Protection { get; set; }
    }

    /// Reads and extracts strings from memory
    public static class ReadMemory
    {
        /// Memory state flag for committed pages
        private const uint MEM_COMMIT = 0x1000;
        /// Protection flag for read write access
        private const uint PAGE_READWRITE = 0x04;

        /// Process access permission flags
        [Flags]
        private enum ProcessAccessFlags : uint
        {
            QueryInformation = 0x0400,
            VirtualMemoryRead = 0x0010,
        }

        /// Win32 memory region information structure
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        /// Opens process handle with access rights
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            ProcessAccessFlags processAccess,
            bool bInheritHandle,
            int processId);

        /// Closes open process or kernel handle
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// Queries memory region information remotely
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int VirtualQueryEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer,
            uint dwLength);

        /// Reads bytes from remote process memory
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead);

        /// Asynchronously extracts strings from process memory
        public static Task<ProcessStringsResult> GetStringsFromProcessAsync(
            int processId,
            int minLength,
            bool asciiOnly,
            CancellationToken cancellationToken)
        {
            return Task.Run(() => GetStringsFromProcessInternal(processId, minLength, asciiOnly, cancellationToken), cancellationToken);
        }

        /// Internal worker for string extraction logic
        private static ProcessStringsResult GetStringsFromProcessInternal(
            int processId,
            int minLength,
            bool asciiOnly,
            CancellationToken cancellationToken)
        {
            var strings = new List<string>();
            var regionInfos = new List<MemoryRegionInfo>();
            IntPtr processHandle = IntPtr.Zero;

            try
            {
                processHandle = OpenProcess(
                    ProcessAccessFlags.QueryInformation | ProcessAccessFlags.VirtualMemoryRead,
                    false,
                    processId);

                if (processHandle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open process with ID {processId}.");
                }

                IntPtr currentAddress = IntPtr.Zero;
                long maxAddress = 0x7FFFFFFFFFFF;

                while (currentAddress.ToInt64() < maxAddress)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (VirtualQueryEx(processHandle, currentAddress, out MEMORY_BASIC_INFORMATION memInfo, (uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) == 0)
                    {
                        break;
                    }

                    long regionSize = (long)memInfo.RegionSize;
                    if (memInfo.State == MEM_COMMIT && (memInfo.Protect & PAGE_READWRITE) != 0)
                    {
                        regionInfos.Add(new MemoryRegionInfo
                        {
                            BaseAddress = (ulong)memInfo.BaseAddress,
                            Size = (ulong)regionSize,
                            Protection = memInfo.Protect.ToString("X")
                        });

                        byte[] buffer = new byte[regionSize];
                        if (ReadProcessMemory(processHandle, memInfo.BaseAddress, buffer, buffer.Length, out int bytesRead) && bytesRead > 0)
                        {
                            strings.AddRange(ExtractAsciiStrings(buffer, minLength));
                            if (!asciiOnly)
                            {
                                strings.AddRange(ExtractUnicodeStrings(buffer, minLength));
                            }
                        }
                    }

                    currentAddress = new IntPtr(memInfo.BaseAddress.ToInt64() + regionSize);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
            }
            finally
            {
                if (processHandle != IntPtr.Zero)
                {
                    CloseHandle(processHandle);
                }
            }
            return new ProcessStringsResult { Strings = strings, Regions = regionInfos };
        }

        /// Extracts ASCII printable strings from buffer
        private static IEnumerable<string> ExtractAsciiStrings(byte[] buffer, int minLength)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < buffer.Length; i++)
            {
                byte b = buffer[i];
                if (b >= 32 && b <= 126)
                {
                    sb.Append((char)b);
                }
                else
                {
                    if (sb.Length >= minLength)
                        yield return sb.ToString();
                    sb.Clear();
                }
            }
            if (sb.Length >= minLength)
                yield return sb.ToString();
        }

        /// Extracts Unicode UTF16 strings from buffer
        private static IEnumerable<string> ExtractUnicodeStrings(byte[] buffer, int minLength)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < buffer.Length - 1; i += 2)
            {
                char c = (char)(buffer[i] | (buffer[i + 1] << 8));
                if (c >= 32 && !char.IsControl(c))
                {
                    sb.Append(c);
                }
                else
                {
                    if (sb.Length >= minLength)
                        yield return sb.ToString();
                    sb.Clear();
                }
            }
            if (sb.Length >= minLength)
                yield return sb.ToString();
        }
    }
}
