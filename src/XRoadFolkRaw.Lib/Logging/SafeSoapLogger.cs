using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace XRoadFolkRaw.Lib.Logging
{
    /// <summary>
    /// Extension methods to safely log SOAP/XML by sanitizing secrets first.
    /// Drop-in: call logger.SafeSoapDebug(xml) instead of logger.LogDebug(xml).
    /// </summary>
    public static partial class SafeSoapLogger
    {
        // EventIds to make logs greppable in prod
        public static readonly EventId SoapRequestEvent = new(41001, "SoapRequest");
        public static readonly EventId SoapResponseEvent = new(41002, "SoapResponse");
        public static readonly EventId SoapGeneralEvent = new(41000, "Soap");

        [GeneratedRegex(@"^[A-Za-z0-9+/=]+$", RegexOptions.Compiled)]
        private static partial Regex Base64Regex();

        [GeneratedRegex(@"^[A-Fa-f0-9]+$", RegexOptions.Compiled)]
        private static partial Regex HexRegex();

        [GeneratedRegex(@"^\d[\d\- ]+$", RegexOptions.Compiled)]
        private static partial Regex NumberRegex();

        [GeneratedRegex(@"(\b(?:password|pwd|token|apikey|apiKey)\s*=\s*['""])([^'""]+)(['""])", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex AttributeRegex();

        /// <summary>
        /// Optional global sanitizer override. If null, DefaultSanitize is used.
        /// You can set this once at app start to plug in your existing SoapSanitizer.
        /// </summary>
        public static Func<string, string>? GlobalSanitizer { get; set; }

        /// <summary>Debug-level safe SOAP log.</summary>
        public static void SafeSoapDebug(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Debug, xml, title, SoapGeneralEvent);
        }

        /// <summary>Information-level safe SOAP log.</summary>
        public static void SafeSoapInfo(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Information, xml, title, SoapGeneralEvent);
        }

        /// <summary>Log a request.</summary>
        public static void SafeSoapRequest(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Debug, xml, title ?? "SOAP Request", SoapRequestEvent);
        }

        /// <summary>Log a response.</summary>
        public static void SafeSoapResponse(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Debug, xml, title ?? "SOAP Response", SoapResponseEvent);
        }

        /// <summary>
        /// Logs an exchange (request, response) as two debug messages, both sanitized.
        /// </summary>
        public static void SafeSoapExchange(this ILogger? logger, string? requestXml, string? responseXml, string? operation = null)
        {
            string op = string.IsNullOrWhiteSpace(operation) ? "" : $" [{operation}]";
            logger.SafeSoapRequest(requestXml, $"SOAP Request{op}");
            logger.SafeSoapResponse(responseXml, $"SOAP Response{op}");
        }

        private static void LogSanitized(ILogger? logger, LogLevel level, string? xml, string? title, EventId evt)
        {
            if (logger == null || !logger.IsEnabled(level))
            {
                return;
            }

            string safe = Sanitize(xml ?? string.Empty);
            if (!string.IsNullOrEmpty(title))
            {
                logger.Log(level, evt, "{Title}\n{Xml}", title, safe);
            }
            else
            {
                logger.Log(level, evt, "{Xml}", safe);
            }
        }

        /// <summary>
        /// Sanitizes sensitive values in SOAP/XML.
        /// Try to use GlobalSanitizer if provided, otherwise a robust default.
        /// </summary>
        public static string Sanitize(string xml)
        {
            if (GlobalSanitizer != null)
            {
                try { return GlobalSanitizer(xml); } catch { /* fall through to default */ }
            }
            return DefaultSanitize(xml);
        }

        // Default sanitizer (prefix-agnostic, robust to invalid XML using regex fallback)
        private static string DefaultSanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // 1) Try XML route first
            try
            {
                XDocument doc = XDocument.Parse(input, LoadOptions.PreserveWhitespace);
                // Element local-names to redact
                HashSet<string> sensitiveNames = new(StringComparer.OrdinalIgnoreCase)
                {
                    "username","user","password","pwd","token","authToken",
                    "ssn","socialsecuritynumber","nationalid","idcode","pin",
                    "publicid","personid","personalcode","secret","apikey","apiKey"
                };

                foreach (XElement? el in doc.Descendants().ToList())
                {
                    if (sensitiveNames.Contains(el.Name.LocalName))
                    {
                        el.Value = Mask(el.Value);
                    }

                    // also mask any attributes on any node that look sensitive
                    foreach (XAttribute? attr in el.Attributes().ToList())
                    {
                        if (sensitiveNames.Contains(attr.Name.LocalName) ||
                            LooksSensitive(attr.Value))
                        {
                            attr.Value = Mask(attr.Value);
                        }
                    }
                }
                return doc.Declaration != null ? doc.Declaration + doc.ToString(SaveOptions.DisableFormatting) : doc.ToString(SaveOptions.DisableFormatting);
            }
            catch
            {
                // 2) Regex fallback (case-insensitive, prefix-agnostic)
                string s = input;
                // redact inner text between start/end tags for known names
                string tagPattern = @"<(?:[\w\-]+:)?({N})(?:\b[^>]*)>(.*?)</(?:[\w\-]+:)?\1\s*>";
                string[] names = [
                    "username","user","password","pwd","token","authToken",
                    "ssn","socialsecuritynumber","nationalid","idcode","pin",
                    "publicid","personid","personalcode","secret","apikey","apiKey"
                ];
                foreach (string n in names)
                {
                    Regex re = new(tagPattern.Replace("{N}", n), RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    s = re.Replace(s, m => m.Value.Replace(m.Groups[2].Value, Mask(m.Groups[2].Value)));
                }

                // redact attribute values that look sensitive
                s = AttributeRegex().Replace(s, m => m.Groups[1].Value + Mask(m.Groups[2].Value) + m.Groups[3].Value);

                return s;
            }
        }

        private static bool LooksSensitive(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            // crude heuristic: longish base64 / hex-ish / number-ish strings
            if (value.Length >= 8 && Base64Regex().IsMatch(value))
            {
                return true; // base64-like
            }

            if (value.Length >= 8 && HexRegex().IsMatch(value))
            {
                return true;   // hex-like
            }

            if (value.Length >= 6 && NumberRegex().IsMatch(value))
            {
                return true;     // number-like
            }

            return false;
        }

        private static string Mask(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }
            // Show only last 2 chars to help correlate, mask the rest
            int visible = Math.Min(2, s.Length);
            return new string('*', Math.Max(0, s.Length - visible)) + s.Substring(s.Length - visible, visible);
        }
    }
}
