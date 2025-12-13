using System;

// System tampering detection and artifact timestamps
namespace SysInfo.Core.Models
{
    // Stores forensic artifact creation dates and counts
    public class TamperingInfo
    {
        public string SrumCreatedDate { get; set; }
        public string AmCacheCreatedDate { get; set; }
        public string DefenderEventLogCreatedDate { get; set; }
        public string LastRecycleBinDeletion { get; set; }
        public string VolumeShadowCopies { get; set; }
        public string OldestPrefetchFile { get; set; }
        public string PrefetchTotalCount { get; set; }
    }
}
