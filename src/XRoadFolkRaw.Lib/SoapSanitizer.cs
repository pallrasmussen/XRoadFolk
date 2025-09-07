using System.Text.RegularExpressions;

namespace XRoadFolkRaw.Lib
{
    public static partial class SoapSanitizer
    {
        [GeneratedRegex("<(?:\\w+:)?username>(.*?)</(?:\\w+:)?username>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex NsUserRegex();

        [GeneratedRegex("<(?:\\w+:)?password>(.*?)</(?:\\w+:)?password>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex NsPassRegex();

        [GeneratedRegex("<(?:\\w+:)?token>(.*?)</(?:\\w+:)?token>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex NsTokenRegex();

        // Avoid backreference; capture opening tag name and match a compatible closing tag explicitly
        [GeneratedRegex("<(?<tag>(?:\\w+:)?userId)>(?<v>.*?)</(?:\\w+:)?userId>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex NsUserIdRegex();

        // Common token aliases in payloads and WS-Security (avoid backreference)
        [GeneratedRegex("<(?<tag>(?:\\w+:)?(?:sessionId|sessionToken|authToken|accessToken|BinarySecurityToken))>(?<v>.*?)</(?:\\w+:)?(?:sessionId|sessionToken|authToken|accessToken|BinarySecurityToken)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex TokenAliasesRegex();

        [GeneratedRegex("<username>(.*?)</username>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex UserRegex();

        [GeneratedRegex("<password>(.*?)</password>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex PassRegex();

        [GeneratedRegex("<token>(.*?)</token>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
        private static partial Regex TokenRegex();

        public static string Scrub(string xml, bool maskTokens = true)
        {
            ArgumentNullException.ThrowIfNull(xml);

            // Namespaced-safe replacements first
            xml = NsUserRegex().Replace(xml, m => $"<username>{LoggingHelper.Mask(m.Groups[1].Value)}</username>");
            xml = NsPassRegex().Replace(xml, m => $"<password>{LoggingHelper.Mask(m.Groups[1].Value)}</password>");
            xml = NsUserIdRegex().Replace(xml, m => $"<{m.Groups["tag"].Value}>{LoggingHelper.Mask(m.Groups["v"].Value)}</{m.Groups["tag"].Value}>");
            xml = NsTokenRegex().Replace(xml, m => maskTokens ? $"<token>{MaskToken(m.Groups[1].Value)}</token>" : $"<token>{m.Groups[1].Value}</token>");
            xml = TokenAliasesRegex().Replace(xml, m => $"<{m.Groups["tag"].Value}>{MaskToken(m.Groups["v"].Value)}</{m.Groups["tag"].Value}>");

            // Backward-compatible non-namespaced fallbacks
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
