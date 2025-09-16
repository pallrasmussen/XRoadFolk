namespace XRoadFolkRaw.Lib.Options
{
    /// <summary>
    /// Request envelope for GetPeoplePublicInfo. Encapsulates the XML template path,
    /// authentication token, common X-Road header context, and optional criteria.
    /// </summary>
    public sealed class GetPeoplePublicInfoRequest
    {
        /// <summary>
        /// Full path to the request XML template used to construct the SOAP envelope.
        /// </summary>
        public string XmlPath { get; set; } = string.Empty;

        /// <summary>
        /// Authentication token passed in the request header.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Common X-Road header values used to populate the SOAP header section.
        /// </summary>
        public XRoadHeaderOptions Header { get; set; } = new XRoadHeaderOptions();

        /// <summary>
        /// Search criteria (all optional; at least one should be supplied).
        /// </summary>
        public string? Ssn { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTimeOffset? DateOfBirth { get; set; }
    }
}
