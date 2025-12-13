using System.Collections.Generic;

/// Process information data models
namespace Processes.Core.Models
{
    /// Basic process identification and metadata
    public class ProcessInfo
    {
        /// Unique process identifier number
        public int Id { get; set; }
        /// Process executable name or display text
        public string Name { get; set; }
        /// Associated Windows service name if applicable
        public string ServiceName { get; set; }

        public ProcessInfo() { }

        public ProcessInfo(int id, string name)
        {
            Id = id;
            Name = name;
        }

        /// Returns formatted process name with ID
        public override string ToString()
        {
            return $"{Name} ({Id})";
        }
    }

    /// Aggregated info for multiple process instances
    public class CombinedProcessInfo : ProcessInfo
    {
        /// All process IDs in this group
        public List<int> Ids { get; }
        /// Formatted display text for UI
        public string Display { get; }

        /// Creates combined process info with grouping
        public CombinedProcessInfo(string name, List<int> ids, string display)
            : base(ids.Count > 0 ? ids[0] : 0, name)
        {
            Ids = ids;
            Display = display;
        }

        /// Returns custom display text
        public override string ToString() => Display;
    }
}