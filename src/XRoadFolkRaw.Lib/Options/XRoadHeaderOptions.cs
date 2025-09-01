namespace XRoadFolkRaw.Lib.Options
{
    /// <summary>
    /// Common header/context for X-Road SOAP requests
    /// </summary>
    public sealed class XRoadHeaderOptions
    {
        public string XId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string ProtocolVersion { get; set; } = string.Empty;

        /// <summary>
        /// Client (SUBSYSTEM)
        /// </summary>
        public string ClientXRoadInstance { get; set; } = string.Empty;
        public string ClientMemberClass { get; set; } = string.Empty;
        public string ClientMemberCode { get; set; } = string.Empty;
        public string ClientSubsystemCode { get; set; } = string.Empty;

        /// <summary>
        /// Service
        /// </summary>
        public string ServiceXRoadInstance { get; set; } = string.Empty;
        public string ServiceMemberClass { get; set; } = string.Empty;
        public string ServiceMemberCode { get; set; } = string.Empty;
        public string ServiceSubsystemCode { get; set; } = string.Empty;
        public string ServiceCode { get; set; } = string.Empty;
        public string ServiceVersion { get; set; } = string.Empty;
    }
}
