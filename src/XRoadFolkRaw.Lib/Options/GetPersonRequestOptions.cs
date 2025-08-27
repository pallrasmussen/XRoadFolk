namespace XRoadFolkRaw.Lib.Options
{
    public sealed class GetPersonRequestOptions
    {
        // Optional identifiers
        public string? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Ssn { get; set; }
        public string? ExternalId { get; set; }

        public IncludeOptions Include { get; set; } = new();

        public sealed class IncludeOptions
        {
            public bool? Addresses { get; set; }
            public bool? AddressesHistory { get; set; }
            public bool? BiologicalParents { get; set; }
            public bool? ChurchMembership { get; set; }
            public bool? ChurchMembershipHistory { get; set; }
            public bool? Citizenships { get; set; }
            public bool? CitizenshipsHistory { get; set; }
            public bool? CivilStatus { get; set; }
            public bool? CivilStatusHistory { get; set; }
            public bool? ForeignSsns { get; set; }
            public bool? Incapacity { get; set; }
            public bool? IncapacityHistory { get; set; }
            public bool? JuridicalChildren { get; set; }
            public bool? JuridicalChildrenHistory { get; set; }
            public bool? JuridicalParents { get; set; }
            public bool? JuridicalParentsHistory { get; set; }
            public bool? Names { get; set; }
            public bool? NamesHistory { get; set; }
            public bool? Notes { get; set; }
            public bool? NotesHistory { get; set; }
            public bool? Postbox { get; set; }
            public bool? SpecialMarks { get; set; }
            public bool? SpecialMarksHistory { get; set; }
            public bool? Spouse { get; set; }
            public bool? SpouseHistory { get; set; }
            public bool? Ssn { get; set; }
            public bool? SsnHistory { get; set; }
        }
    }
}