using System;
using System.Collections.Generic;
using Process.NET;
using Process.NET.Memory;
using Process.NET.Native.Types;
using Processes.Core.Models;

/// Process memory analysis utilities
namespace Processes.Core.Util
{
    /// Scans and validates process memory regions
    public static class MemoryRegionScanner
    {
        /// Minimum acceptable memory region size bytes
        private const int MinRegionSize = 4096;
        /// Maximum acceptable memory region size bytes
        private const int MaxRegionSize = 512 * 1024 * 1024;

        /// Returns readable committed memory regions only
        public static IEnumerable<MemoryRegionInfo> GetValidRegions(IProcess process)
        {
            foreach (var region in process.MemoryFactory.Regions)
            {
                var info = region.Information;

                if (!IsValidState(info.State))
                    continue;
                if (!IsValidType(info.Type))
                    continue;
                if (!IsReadable(info.Protect))
                    continue;
                if (!IsValidSize((long)info.RegionSize))
                    continue;

                yield return new MemoryRegionInfo
                {
                    BaseAddress = (ulong)region.BaseAddress.ToInt64(),
                    Size = (ulong)info.RegionSize,
                    Protection = info.Protect.ToString()
                };
            }
        }

        /// Reads raw bytes from specified region
        public static byte[] ReadRegion(IProcess process, MemoryRegionInfo region)
        {
            return process.Memory.Read(new IntPtr((long)region.BaseAddress), (int)region.Size);
        }

        /// Checks if memory state is committed
        private static bool IsValidState(MemoryStateFlags state)
        {
            return state == MemoryStateFlags.Commit;
        }

        /// Checks if memory type is scannable
        private static bool IsValidType(MemoryTypeFlags type)
        {
            return type == MemoryTypeFlags.Private || type == MemoryTypeFlags.Image;
        }

        /// Verifies region has read permissions
        private static bool IsReadable(MemoryProtectionFlags protect)
        {
            if (protect.HasFlag(MemoryProtectionFlags.Guard) || protect.HasFlag(MemoryProtectionFlags.NoAccess))
                return false;

            return protect.HasFlag(MemoryProtectionFlags.ReadOnly) ||
                   protect.HasFlag(MemoryProtectionFlags.ReadWrite) ||
                   protect.HasFlag(MemoryProtectionFlags.ExecuteRead) ||
                   protect.HasFlag(MemoryProtectionFlags.ExecuteReadWrite);
        }

        /// Validates region size within acceptable range
        private static bool IsValidSize(long regionSize)
        {
            return regionSize >= MinRegionSize && regionSize <= MaxRegionSize;
        }
    }
}
