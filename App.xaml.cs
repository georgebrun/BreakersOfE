using BreakersOfE.Services;
using BreakersOfE.Windows;
using System.Windows;

namespace BreakersOfE
{
    public partial class App : Application
    {
        internal static SplashWindow? Splash { get; set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Apply saved theme before any window opens
            ThemeService.ApplySavedTheme();

            // Show splash screen
            Splash = new SplashWindow();
            Splash.Show();
            Splash.SetStatus("Starting up...", 5);
        }
    }
}