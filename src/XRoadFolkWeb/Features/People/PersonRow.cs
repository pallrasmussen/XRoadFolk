namespace XRoadFolkWeb.Features.People
{
    /// <summary>
    /// Minimal view model representing a person row extracted from GetPeoplePublicInfo.
    /// </summary>
    public sealed class PersonRow
    {
        /// <summary>Public identifier of the person, when available.</summary>
        public string? PublicId { get; set; }
        /// <summary>SSN when available (may be masked upstream).</summary>
        public string? SSN { get; set; }
        /// <summary>First name(s) derived from Name items.</summary>
        public string? FirstName { get; set; }
        /// <summary>Last name derived from Name items.</summary>
        public string? LastName { get; set; }
        /// <summary>Date of birth when present; may be yyyy-MM-dd.</summary>
        public string? DateOfBirth { get; set; }
    }
}
