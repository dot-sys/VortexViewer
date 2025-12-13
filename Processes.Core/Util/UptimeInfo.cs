using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

/// Process memory analysis utilities
namespace Processes.Core.Util
{
    /// Stores process name and uptime info
    public class ProcessUptimeInfo
    {
        /// Display name of the process
        public string Name { get; }
        /// Formatted uptime duration string
        public string Uptime { get; }

        /// Creates uptime info with values
        public ProcessUptimeInfo(string name, string uptime)
        {
            Name = name;
            Uptime = uptime;
        }

        /// Returns formatted name and uptime
        public override string ToString() => $"{Name,-16} {Uptime}";
    }

    /// Collects uptime for target processes
    public static class ProcessUptimeCollector
    {
        /// List of monitored process names
        private static readonly string[] TargetProcesses = new[]
        {
            "AggregatorHost", "DiagTrack", "DnsCache", "DPS", "DusmSvc", "DWM",
            "Eventlog", "Explorer", "Lsass", "PcaSvc", "Sysmain", "WSearch"
        };

        /// Gets uptime for all target processes
        public static List<ProcessUptimeInfo> GetTargetProcessUptimes()
        {
            var processes = System.Diagnostics.Process.GetProcesses();
            var exactMatches = BuildExactMatches(processes);
            var servicePidMap = BuildServicePidMap();

            return TargetProcesses
                .Select(target => CreateUptimeInfo(target, exactMatches, servicePidMap, processes))
                .ToList();
        }

        /// Matches running processes to target names
        private static Dictionary<string, System.Diagnostics.Process> BuildExactMatches(System.Diagnostics.Process[] processes)
        {
            var matches = new Dictionary<string, System.Diagnostics.Process>();
            foreach (var proc in processes)
            {
                var match = TargetProcesses.FirstOrDefault(target =>
                    string.Equals(proc.ProcessName, target, StringComparison.OrdinalIgnoreCase));
                if (match != null && !matches.ContainsKey(match))
                {
                    matches[match] = proc;
                }
            }
            return matches;
        }

        /// Maps service names to process IDs
        private static Dictionary<string, int> BuildServicePidMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
                            map[name] = Convert.ToInt32(pidObj);
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        /// Creates uptime info for target process
        private static ProcessUptimeInfo CreateUptimeInfo(
            string target,
            Dictionary<string, System.Diagnostics.Process> exactMatches,
            Dictionary<string, int> servicePidMap,
            System.Diagnostics.Process[] processes)
        {
            string displayName = ExtractDisplayName(target);
            string uptime = GetUptimeString(target, exactMatches, servicePidMap, processes);
            return new ProcessUptimeInfo(displayName, uptime);
        }

        /// Extracts display name from service name
        private static string ExtractDisplayName(string name)
        {
            if (name.Contains("[") && name.Contains("]"))
            {
                int start = name.IndexOf('[') + 1;
                int end = name.IndexOf(']');
                if (end > start)
                    return name.Substring(start, end - start);
            }
            return name;
        }

        /// Calculates formatted uptime or status text
        private static string GetUptimeString(
            string target,
            Dictionary<string, System.Diagnostics.Process> exactMatches,
            Dictionary<string, int> servicePidMap,
            System.Diagnostics.Process[] processes)
        {
            if (exactMatches.TryGetValue(target, out var proc))
            {
                return GetFormattedUptime(proc);
            }

            if (servicePidMap.TryGetValue(target, out var pid))
            {
                var svchostProc = processes.FirstOrDefault(p => p.Id == pid);
                if (svchostProc != null)
                {
                    return GetFormattedUptime(svchostProc);
                }
            }

            return "Not running";
        }

        /// Formats process uptime as days hours mins
        private static string GetFormattedUptime(System.Diagnostics.Process proc)
        {
            try
            {
                var uptime = DateTime.Now - proc.StartTime;
                return $"{(int)uptime.TotalDays} days, {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
            }
            catch
            {
                return "N/A";
            }
        }
    }
}