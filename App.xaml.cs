using BreakersOfE.Services;
using System.Windows;

namespace BreakersOfE
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Apply saved theme before any window opens
            ThemeService.ApplySavedTheme();
        }
    }
}
