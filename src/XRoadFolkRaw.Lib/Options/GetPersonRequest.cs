namespace XRoadFolkRaw.Lib.Options
{
    public sealed class GetPersonRequest
    {
        public string XmlPath { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public XRoadHeaderOptions Header { get; set; } = new XRoadHeaderOptions();

        // Identifiers
        public string? PublicId { get; set; }
        public string? Ssn { get; set; }
        public string? Id { get; set; }
        public string? ExternalId { get; set; }

        // Include flags
        public GetPersonInclude Include { get; set; } = GetPersonInclude.None;
    }
}
