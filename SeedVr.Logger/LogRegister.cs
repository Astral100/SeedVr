using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace SeedVr.Logger
{
    public class LogRegister
    {
        public static void CreateLogger()
        {
            // Message:lj renders values as they are. Without the l, Serilog writes a string as a quoted JSON
            // literal, so a JSON response comes back with every inner quote escaped.
            var outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{CallerFileName}.{CallerMethodName}] {Message:lj}{NewLine}{Exception}";
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: outputTemplate, theme: CreateConsoleTheme())
                .Enrich.FromLogContext()
                .CreateLogger();
        }

        /// <summary>The sink's own theme with the levels recoloured, so structured values keep the colours they had.</summary>
        private static SystemConsoleTheme CreateConsoleTheme()
        {
            // Literate leaves Information the same white as the message text, which is what made the level
            // invisible. Everything not listed here - String, Number and the rest - stays as Literate has it.
            var styles = new Dictionary<ConsoleThemeStyle, SystemConsoleThemeStyle>(SystemConsoleTheme.Literate.Styles)
            {
                [ConsoleThemeStyle.LevelInformation] = new() { Foreground = ConsoleColor.DarkGreen },
                [ConsoleThemeStyle.LevelWarning] = new() { Foreground = ConsoleColor.Yellow },
                [ConsoleThemeStyle.LevelError] = new() { Foreground = ConsoleColor.Red },
                [ConsoleThemeStyle.LevelFatal] = new() { Foreground = ConsoleColor.White, Background = ConsoleColor.Red }
            };

            var consoleTheme = new SystemConsoleTheme(styles);
            return consoleTheme;
        }

        public static void DisposeLogger()
        {
            Serilog.Log.CloseAndFlush();
        }
    }
}