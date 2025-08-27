namespace XRoadFolkRaw.Lib.Options
{
    public sealed class GetPersonRequestOptions
    {
        // Optional identifiers
        public string? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Ssn { get; set; }
        public string? ExternalId { get; set; }

        // Optional sections to include in the response. Values can be either
        // exact element names (e.g., "IncludeAddresses", "IncludeSsnHistory")
        // or shorthand names (e.g., "addresses", "ssnHistory"), which will be
        // normalized by the client to element names.
        public HashSet<string> Include { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}