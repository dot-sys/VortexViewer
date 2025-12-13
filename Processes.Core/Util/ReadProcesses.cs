using System;
using System.Collections.Generic;
using System.Management;
using Processes.Core.Models;

/// Process memory analysis utilities
namespace Processes.Core.Util
{
    /// Enumerates running processes with service details
    public static class ReadProcesses
    {
        /// Returns all running processes with metadata
        public static List<ProcessInfo> GetRunningProcesses()
        {
            var result = new List<ProcessInfo>();
            var svchostServiceMap = GetSvchostServiceMap();

            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (proc.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase))
                    {
                        AddSvchostEntries(result, proc, svchostServiceMap);
                    }
                    else
                    {
                        result.Add(new ProcessInfo { Id = proc.Id, Name = proc.ProcessName });
                    }
                }
                catch { }
            }
            return result;
        }

        /// Adds svchost entries with service names
        private static void AddSvchostEntries(List<ProcessInfo> result, System.Diagnostics.Process proc, Dictionary<int, List<string>> serviceMap)
        {
            if (serviceMap.TryGetValue(proc.Id, out var serviceNames) && serviceNames.Count > 0)
            {
                foreach (var serviceName in serviceNames)
                {
                    result.Add(new ProcessInfo
                    {
                        Id = proc.Id,
                        Name = $"svchost [{serviceName}]",
                        ServiceName = serviceName
                    });
                }
            }
            else
            {
                result.Add(new ProcessInfo { Id = proc.Id, Name = "svchost" });
            }
        }

        /// Maps process IDs to hosted services
        private static Dictionary<int, List<string>> GetSvchostServiceMap()
        {
            var map = new Dictionary<int, List<string>>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId > 0"))
                {
                    foreach (ManagementObject service in searcher.Get())
                    {
                        var name = service["Name"]?.ToString();
                        var pidObj = service["ProcessId"];
                        if (!string.IsNullOrEmpty(name) && pidObj != null)
                        {
                            int pid = Convert.ToInt32(pidObj);
                            if (!map.ContainsKey(pid))
                                map[pid] = new List<string>();
                            map[pid].Add(name);
                        }
                    }
                }
            }
            catch { }
            return map;
        }
    }
}