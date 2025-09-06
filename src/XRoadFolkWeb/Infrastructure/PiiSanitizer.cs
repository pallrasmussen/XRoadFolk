using System.Text.RegularExpressions;
using XRoadFolkRaw.Lib;
using XRoadFolkRaw.Lib.Logging;

namespace XRoadFolkWeb.Infrastructure
{
    internal static partial class PiiSanitizer
    {
        [GeneratedRegex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.Compiled)]
        private static partial Regex GuidRegex();

        [GeneratedRegex(@"\b\d{6,}\b", RegexOptions.Compiled)]
        private static partial Regex LongDigitsRegex();

        [GeneratedRegex(@"(?i)(Authorization\s*:\s*Bearer\s+)([^\s]+)")] 
        private static partial Regex BearerRegex();

        [GeneratedRegex(@"(?i)(Api[-_ ]?Key\s*[:=]\s*)([^\s]+)")] 
        private static partial Regex ApiKeyRegex();

        [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex EmailRegex();

        public static string Sanitize(string? input, bool maskTokens)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            // If looks like XML/SOAP, use SOAP sanitizer which respects maskTokens
            string s = input;
            if (LooksLikeXml(s))
            {
                try { return SafeSoapLogger.Sanitize(s); } catch { return s; }
            }

            // Generic masking
            s = GuidRegex().Replace(s, m => LoggingHelper.Mask(m.Value));
            s = LongDigitsRegex().Replace(s, m => LoggingHelper.Mask(m.Value));
            s = EmailRegex().Replace(s, m => MaskEmail(m.Value));
            if (maskTokens)
            {
                s = BearerRegex().Replace(s, m => m.Groups[1].Value + LoggingHelper.Mask(m.Groups[2].Value, 6));
                s = ApiKeyRegex().Replace(s, m => m.Groups[1].Value + LoggingHelper.Mask(m.Groups[2].Value, 6));
            }
            return s;
        }

        private static bool LooksLikeXml(string s)
        {
            s = s.TrimStart();
            return s.StartsWith("<", StringComparison.Ordinal) || s.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
        }

        private static string MaskEmail(string email)
        {
            int at = email.IndexOf('@');
            if (at <= 1)
            {
                return "***@***";
            }
            string user = email[..at];
            string domain = email[(at + 1)..];
            return LoggingHelper.Mask(user, 1) + "@" + LoggingHelper.Mask(domain, 3);
        }
    }
}
