using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using XRoadFolkRaw.Lib;

namespace XRoadFolkRaw.Lib.Logging
{
    /// <summary>
    /// Extension methods to safely log SOAP/XML by sanitizing secrets first.
    /// Drop-in: call logger.SafeSoapDebug(xml) instead of logger.LogDebug(xml).
    /// </summary>
    public static partial class SafeSoapLogger
    {
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
                    _logRequestWithTitle(logger, title!, safe, arg4: null);
                }
                else if (evt == SoapResponseEvent)
                {
                    _logResponseWithTitle(logger, title!, safe, arg4: null);
                }
                else if (level == LogLevel.Information)
                {
                    _logInfoWithTitle(logger, title!, safe, arg4: null);
                }
                else
                {
                    _logGeneralWithTitle(logger, title!, safe, arg4: null);
                }
            }
            else if (level == LogLevel.Information)
            {
                _logInfo(logger, safe, arg3: null);
            }
            else
            {
                _logGeneral(logger, safe, arg3: null);
            }
        }

        /// <summary>
        /// Sanitizes sensitive values in SOAP/XML.
        /// Try to use GlobalSanitizer if provided, otherwise a robust default.
        /// </summary>
        /// <param name="xml"></param>
        public static string Sanitize(string xml)
        {
            if (GlobalSanitizer != null)
            {
                try { return GlobalSanitizer(xml); } catch { /* fall through to default */ }
            }
            return DefaultSanitize(xml);
        }

        /// <summary>
        /// Default sanitizer: delegate to SoapSanitizer.Scrub to keep behavior centralized.
        /// </summary>
        private static string DefaultSanitize(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            try
            {
                // Mask tokens by default; can be overridden via GlobalSanitizer
                return SoapSanitizer.Scrub(input, maskTokens: true);
            }
            catch
            {
                // Never throw from logging helpers
                return input;
            }
        }
    }
}
