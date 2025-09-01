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
        /// <summary>
        /// EventIds to make logs greppable in prod.
        /// </summary>
        public static readonly EventId SoapRequestEvent = new(41001, "SoapRequest");
        public static readonly EventId SoapResponseEvent = new(41002, "SoapResponse");
        public static readonly EventId SoapGeneralEvent = new(41000, "Soap");

        private static readonly Action<ILogger, string, Exception?> _logGeneral =
            LoggerMessage.Define<string>(LogLevel.Debug, SoapGeneralEvent, "{Xml}");
        private static readonly Action<ILogger, string, string, Exception?> _logGeneralWithTitle =
            LoggerMessage.Define<string, string>(LogLevel.Debug, SoapGeneralEvent, "{Title}\n{Xml}");
        private static readonly Action<ILogger, string, Exception?> _logInfo =
            LoggerMessage.Define<string>(LogLevel.Information, SoapGeneralEvent, "{Xml}");
        private static readonly Action<ILogger, string, string, Exception?> _logInfoWithTitle =
            LoggerMessage.Define<string, string>(LogLevel.Information, SoapGeneralEvent, "{Title}\n{Xml}");
        private static readonly Action<ILogger, string, string, Exception?> _logRequestWithTitle =
            LoggerMessage.Define<string, string>(LogLevel.Debug, SoapRequestEvent, "{Title}\n{Xml}");
        private static readonly Action<ILogger, string, string, Exception?> _logResponseWithTitle =
            LoggerMessage.Define<string, string>(LogLevel.Debug, SoapResponseEvent, "{Title}\n{Xml}");

        [GeneratedRegex(@"^[A-Za-z0-9+/=]+$", RegexOptions.Compiled)]
        private static partial Regex Base64Regex();

        [GeneratedRegex(@"^[A-Fa-f0-9]+$", RegexOptions.Compiled)]
        private static partial Regex HexRegex();

        [GeneratedRegex(@"^\d[\d\- ]+$", RegexOptions.Compiled)]
        private static partial Regex NumberRegex();

        [GeneratedRegex(@"(\b(?:password|pwd|token|apikey|apiKey)\s*=\s*['""])([^'""]+)(['""])", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex AttributeRegex();

        private static readonly string[] SensitiveElementNames =
        [
            "username", "user", "password", "pwd", "token", "authToken",
            "ssn", "socialsecuritynumber", "nationalid", "idcode", "pin",
            "publicid", "personid", "personalcode", "secret", "apikey", "apiKey",
        ];

        private static readonly HashSet<string> SensitiveNames =
            new(SensitiveElementNames, StringComparer.OrdinalIgnoreCase);

        private const string TagPatternTemplate =
            "<(?:[\\w\\-]+:)?({N})(?:\\b[^>]*)>(.*?)</(?:[\\w\\-]+:)?\\1\\s*>";

        private static readonly Dictionary<string, Regex> TagRegexCache =
            SensitiveElementNames.ToDictionary(
                n => n,
                n => new Regex(
                    TagPatternTemplate.Replace("{N}", n, StringComparison.Ordinal),
                    RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled), StringComparer.Ordinal);

        /// <summary>
        /// Optional global sanitizer override. If null, DefaultSanitize is used.
        /// You can set this once at app start to plug in your existing SoapSanitizer.
        /// </summary>
        public static Func<string, string>? GlobalSanitizer { get; set; }

        /// <summary>
        /// Debug-level safe SOAP log.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="xml">The SOAP/XML string to log.</param>
        /// <param name="title">Optional log title.</param>
        public static void SafeSoapDebug(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Debug, xml, title, SoapGeneralEvent);
        }

        /// <summary>
        /// Information-level safe SOAP log.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="xml">The SOAP/XML string to log.</param>
        /// <param name="title">Optional log title.</param>
        public static void SafeSoapInfo(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Information, xml, title, SoapGeneralEvent);
        }

        /// <summary>Log a request.</summary>
        /// <param name="logger"></param>
        /// <param name="xml"></param>
        /// <param name="title"></param>
        public static void SafeSoapRequest(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Debug, xml, title ?? "SOAP Request", SoapRequestEvent);
        }

        /// <summary>Log a response.</summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="xml">The SOAP/XML string to log.</param>
        /// <param name="title">Optional log title.</param>
        public static void SafeSoapResponse(this ILogger? logger, string? xml, string? title = null)
        {
            LogSanitized(logger, LogLevel.Debug, xml, title ?? "SOAP Response", SoapResponseEvent);
        }

        /// <summary>
        /// Logs an exchange (request, response) as two debug messages, both sanitized.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="requestXml"></param>
        /// <param name="responseXml"></param>
        /// <param name="operation"></param>
        public static void SafeSoapExchange(this ILogger? logger, string? requestXml, string? responseXml, string? operation = null)
        {
            string op = string.IsNullOrWhiteSpace(operation) ? "" : $" [{operation}]";
            logger.SafeSoapRequest(requestXml, $"SOAP Request{op}");
            logger.SafeSoapResponse(responseXml, $"SOAP Response{op}");
        }

        private static void LogSanitized(ILogger? logger, LogLevel level, string? xml, string? title, EventId evt)
        {
            if (logger?.IsEnabled(level) != true)
            {
                return;
            }

            string safe = Sanitize(xml ?? string.Empty);
            if (!string.IsNullOrEmpty(title))
            {
                if (evt == SoapRequestEvent)
                {
                    _logRequestWithTitle(logger, title!, safe, null);
                }
                else if (evt == SoapResponseEvent)
                {
                    _logResponseWithTitle(logger, title!, safe, null);
                }
                else if (level == LogLevel.Information)
                {
                    _logInfoWithTitle(logger, title!, safe, null);
                }
                else
                {
                    _logGeneralWithTitle(logger, title!, safe, null);
                }
            }
            else if (level == LogLevel.Information)
            {
                _logInfo(logger, safe, null);
            }
            else
            {
                _logGeneral(logger, safe, null);
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

        /// <summary>
        /// Default sanitizer (prefix-agnostic, robust to invalid XML using regex fallback)
        /// </summary>
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

                foreach (XElement? el in doc.Descendants().ToList())
                {
                    if (SensitiveNames.Contains(el.Name.LocalName))
                    {
                        el.Value = Mask(el.Value);
                    }

                    // also mask any attributes on any node that look sensitive
                    foreach (XAttribute? attr in el.Attributes().ToList())
                    {
                        if (SensitiveNames.Contains(attr.Name.LocalName) ||
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

                foreach (Regex re in TagRegexCache.Values)
                {
                    s = re.Replace(s, m => m.Value.Replace(m.Groups[2].Value, Mask(m.Groups[2].Value), StringComparison.Ordinal));
                }

                // redact attribute values that look sensitive
                return AttributeRegex().Replace(s, m => m.Groups[1].Value + Mask(m.Groups[2].Value) + m.Groups[3].Value);
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
            int masked = Math.Max(0, s.Length - visible);
            return string.Concat(new string('*', masked), s.AsSpan(s.Length - visible, visible));
        }
    }
}
