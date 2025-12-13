using System;
using System.Collections.Concurrent;

// String interning compatibility layer
namespace Timeline.Core.Util
{
    // Legacy string pool (deprecated)
    public static class StringPool
    {
        public static string InternDescription(string value)
        {
            return value;
        }

        public static string InternSource(string value)
        {
            return value;
        }

        public static string InternOtherInfo(string value)
        {
            return value;
        }

        public static string InternPath(string value)
        {
            return value;
        }

        public static string GetStatistics()
        {
            return "StringPool is deprecated. Interning handled by PathCleaner.CleanAllPaths()";
        }

        public static void Clear()
        {
        }
    }
}
