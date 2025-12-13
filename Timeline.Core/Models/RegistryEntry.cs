using System;
using System.Text;

// Timeline artifact data models
namespace Timeline.Core.Models
{
    public enum HiveType
    {
        NTUSER,
        USRCLASS,
        SOFTWARE,
        SYSTEM,
        Amcache
    }

    // Timeline entry with timestamp and metadata
    public class RegistryEntry
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Source { get; set; }
        public string Description { get; set; }
        public string Path { get; set; }
        public string OtherInfo { get; set; }
        
        // File signature verification status
        public string Signed { get; set; }
        
        // File modification detection status
        public string Modified { get; set; }
        
        // Certificate common name
        public string CN { get; set; }
        // Certificate organizational unit
        public string OU { get; set; }
        // Certificate state or province
        public string S { get; set; }
        // Certificate serial number prefix
        public string Serial { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fffffff zzz}");
            sb.AppendLine($"Source: {Source}");
            sb.AppendLine($"Description: {Description}");
            sb.AppendLine($"Path: {Path}");
            if (!string.IsNullOrEmpty(OtherInfo))
            {
                sb.AppendLine($"Other Info: {OtherInfo}");
            }
            return sb.ToString();
        }
    }
}