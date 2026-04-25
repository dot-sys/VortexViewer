using System;
using System.Windows;

// Application entry point
namespace Vortex.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string resourceName = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
                string resourcePath = "Vortex.UI.Resources.Embedded." + resourceName;

                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath))
                {
                    if (stream == null) return null;
                    byte[] assemblyData = new byte[stream.Length];
                    stream.Read(assemblyData, 0, assemblyData.Length);
                    return System.Reflection.Assembly.Load(assemblyData);
                }
            };

            // Suppress cryptographic validation exceptions from corrupted registry values
            AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            {
                if (args.Exception is System.Security.Cryptography.CryptographicException)
                {
                    // Suppress cryptographic exceptions during registry parsing
                }
            };

            base.OnStartup(e);
        }
    }
}