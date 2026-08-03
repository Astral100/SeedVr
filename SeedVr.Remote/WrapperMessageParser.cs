using System.Globalization;
using System.Text.RegularExpressions;

namespace SeedVr.Remote
{
    /// <summary>Parses the percent out of the wrapper's human-readable /result message, e.g. "Progress: 70.0% (70/100)".</summary>
    public static class WrapperMessageParser
    {
        private static readonly Regex PercentPattern = new Regex(@"Progress:\s*(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The message's percent, or null when the message carries none.</summary>
        public static double? ParsePercent(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            var match = PercentPattern.Match(message);
            if (!match.Success)
            {
                return null;
            }

            var percent = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return percent;
        }
    }
}
