using System;
using System.Windows;

// Application entry point
namespace Vortex.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
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