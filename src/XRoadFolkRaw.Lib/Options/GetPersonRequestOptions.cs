namespace XRoadFolkRaw.Lib.Options
{
    [Flags]
    public enum GetPersonInclude
    {
        None = 0,
        Addresses = 1 << 0,
        AddressesHistory = 1 << 1,
        BiologicalParents = 1 << 2,
        ChurchMembership = 1 << 3,
        ChurchMembershipHistory = 1 << 4,
        Citizenships = 1 << 5,
        CitizenshipsHistory = 1 << 6,
        CivilStatus = 1 << 7,
        CivilStatusHistory = 1 << 8,
        ForeignSsns = 1 << 9,
        Incapacity = 1 << 10,
        IncapacityHistory = 1 << 11,
        JuridicalChildren = 1 << 12,
        JuridicalChildrenHistory = 1 << 13,
        JuridicalParents = 1 << 14,
        JuridicalParentsHistory = 1 << 15,
        Names = 1 << 16,
        NamesHistory = 1 << 17,
        Notes = 1 << 18,
        NotesHistory = 1 << 19,
        Postbox = 1 << 20,
        SpecialMarks = 1 << 21,
        SpecialMarksHistory = 1 << 22,
        Spouse = 1 << 23,
        SpouseHistory = 1 << 24,
        Ssn = 1 << 25,
        SsnHistory = 1 << 26
    }

    public sealed class GetPersonRequestOptions
    {
        // Optional identifiers
        public string? Id { get; set; }
        public string? PublicId { get; set; }
        public string? Ssn { get; set; }
        public string? ExternalId { get; set; }

        // Type-safe include flags for supported sections
        public GetPersonInclude Include { get; set; } = GetPersonInclude.None;
    }
}