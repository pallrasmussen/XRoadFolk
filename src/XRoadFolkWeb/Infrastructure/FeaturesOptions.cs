using System.ComponentModel.DataAnnotations;

namespace XRoadFolkWeb.Infrastructure
{
    public sealed class FeaturesOptions
    {
        /// <summary>
        /// If true, include detailed exception info in responses. If null, defaults to Development=true, Production=false.
        /// Key: Features:DetailedErrors
        /// </summary>
        public bool? DetailedErrors { get; set; }

        /// <summary>
        /// Controls visibility of Logs button in the UI. If null, defaults to true.
        /// Key: Features:ShowLogs
        /// </summary>
        public bool? ShowLogs { get; set; }

        /// <summary>
        /// Nested options for HTTP/Application logs endpoints.
        /// </summary>
        [Required]
        public LogsOptions Logs { get; set; } = new();

        public sealed class LogsOptions
        {
            /// <summary>
            /// Enables /logs and /logs/stream endpoints. If null, defaults to true in Development, false otherwise.
            /// Key: Features:Logs:Enabled
            /// </summary>
            public bool? Enabled { get; set; }
        }
    }
}
