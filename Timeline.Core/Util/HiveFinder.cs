using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Registry hive location utilities
namespace Timeline.Core.Util
{
    // Locates registry hives on disk
    public static class HiveFinder
    {
        public static Dictionary<string, string> FindHivesInStandardLocations()
        {
            var hives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);

            var systemHivePath = Path.Combine(systemRoot, @"config\SYSTEM");
            var softwareHivePath = Path.Combine(systemRoot, @"config\SOFTWARE");
            
            hives.Add("SYSTEM", systemHivePath);
            
            hives.Add("SOFTWARE", softwareHivePath);
            
            var currentUser = Environment.UserName;
            var currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            AddHiveIfFound(hives, $"NTUSER.DAT_{currentUser}", Path.Combine(currentUserProfile, "NTUSER.DAT"));

            var usrClassPath = Path.Combine(currentUserProfile, @"AppData\Local\Microsoft\Windows\UsrClass.dat");
            AddHiveIfFound(hives, $"UsrClass.dat_{currentUser}", usrClassPath);

            return hives;
        }

        private static void AddHiveIfFound(Dictionary<string, string> hives, string name, string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists)
                {
                    if (!hives.ContainsKey(name))
                    {
                        hives.Add(name, path);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (!hives.ContainsKey(name))
                {
                    hives.Add(name, path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}