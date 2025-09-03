using System.Text.RegularExpressions;

namespace XRoadFolkRaw.Lib
{
    public static partial class SoapSanitizer
    {
        [GeneratedRegex("<username>(.*?)</username>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
        private static partial Regex UserRegex();

        [GeneratedRegex("<password>(.*?)</password>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
        private static partial Regex PassRegex();

        [GeneratedRegex("<token>(.*?)</token>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
        private static partial Regex TokenRegex();

        public static string Scrub(string xml, bool maskTokens = true)
        {
            ArgumentNullException.ThrowIfNull(xml);
            xml = UserRegex().Replace(xml, m => $"<username>{LoggingHelper.Mask(m.Groups[1].Value)}</username>");
            xml = PassRegex().Replace(xml, m => $"<password>{LoggingHelper.Mask(m.Groups[1].Value)}</password>");
            xml = TokenRegex().Replace(xml, m => maskTokens
                ? $"<token>{MaskToken(m.Groups[1].Value)}</token>"
                : $"<token>{m.Groups[1].Value}</token>");
            return xml;
        }

        private static string MaskToken(string s)
        {
            return LoggingHelper.Mask(s, 6);
        }
    }
}
