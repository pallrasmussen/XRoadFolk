namespace XRoadFolkRaw.Lib.Options
{
    public sealed class GetPersonRequest
    {
        public string XmlPath { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public XRoadHeaderOptions Header { get; set; } = new XRoadHeaderOptions();

        /// <summary>
        /// Identifiers
        /// </summary>
        public string? PublicId { get; set; }
        public string? Ssn { get; set; }
        public string? Id { get; set; }
        public string? ExternalId { get; set; }

        /// <summary>
        /// Include flags
        /// </summary>
        public GetPersonInclude Include { get; set; } = GetPersonInclude.None;
    }
}
