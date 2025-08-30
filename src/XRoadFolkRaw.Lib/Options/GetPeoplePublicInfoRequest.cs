namespace XRoadFolkRaw.Lib.Options
{
    public sealed class GetPeoplePublicInfoRequest
    {
        public string XmlPath { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public XRoadHeaderOptions Header { get; set; } = new XRoadHeaderOptions();

        // Criteria
        public string? Ssn { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTimeOffset? DateOfBirth { get; set; }
    }
}
