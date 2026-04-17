using BreakersOfE.Data;
using System;
using System.Linq;
using System.Windows;

namespace BreakersOfE.Services
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public static class ThemeService
    {
        private const string SettingKey = "Theme";
        private const string LightThemePath =
            "pack://application:,,,/BreakersOfE;component/Themes/LightTheme.xaml";
        private const string DarkThemePath =
            "pack://application:,,,/BreakersOfE;component/Themes/DarkTheme.xaml";

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        // ── Apply theme at startup ────────────────────────────────────────
        public static void ApplySavedTheme()
        {
            AppTheme theme = AppTheme.Light; // default

            try
            {
                using var db = new AppDbContext();
                var setting = db.AppSettings
                    .FirstOrDefault(s => s.Key == SettingKey);

                if (setting != null &&
                    Enum.TryParse<AppTheme>(setting.Value, out var saved))
                {
                    theme = saved;
                }
            }
            catch { /* use default if DB not ready */ }

            Apply(theme);
        }

        // ── Toggle between light and dark ─────────────────────────────────
        public static void Toggle()
        {
            Apply(CurrentTheme == AppTheme.Light
                ? AppTheme.Dark
                : AppTheme.Light);
        }

        // ── Apply a specific theme ────────────────────────────────────────
        public static void Apply(AppTheme theme)
        {
            CurrentTheme = theme;

            string path = theme == AppTheme.Dark
                ? DarkThemePath
                : LightThemePath;

            var dict = new ResourceDictionary
            {
                Source = new Uri(path, UriKind.Absolute)
            };

            // Replace the theme dictionary in App resources
            var existing = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d =>
                    d.Source != null &&
                    (d.Source.OriginalString.Contains("LightTheme") ||
                     d.Source.OriginalString.Contains("DarkTheme")));

            if (existing != null)
                Application.Current.Resources.MergedDictionaries
                    .Remove(existing);

            Application.Current.Resources.MergedDictionaries.Add(dict);

            SaveTheme(theme);
        }

        // ── Persist to DB ─────────────────────────────────────────────────
        private static void SaveTheme(AppTheme theme)
        {
            try
            {
                using var db = new AppDbContext();
                var setting = db.AppSettings
                    .FirstOrDefault(s => s.Key == SettingKey);

                if (setting == null)
                {
                    db.AppSettings.Add(new Models.AppSetting
                    {
                        Key = SettingKey,
                        Value = theme.ToString()
                    });
                }
                else
                {
                    setting.Value = theme.ToString();
                }

                db.SaveChanges();
            }
            catch { /* non-fatal */ }
        }

        // ── Helper for toolbar icon ───────────────────────────────────────
        public static string ThemeToggleIcon =>
            CurrentTheme == AppTheme.Light ? "\uE708" : "\uE706";
        // E708 = moon (switch to dark), E706 = sun (switch to light)

        public static string ThemeToggleTooltip =>
            CurrentTheme == AppTheme.Light
                ? "Switch to Dark Mode"
                : "Switch to Light Mode";
    }
}
