#nullable enable
// This is a minimal strongly-typed resource wrapper for DataAnnotations error messages.
namespace XRoadFolkWeb.Resources
{
    using System.Globalization;
    using System.Resources;

    public static class ValidationMessages
    {
        private static ResourceManager? resourceMan;
        private static CultureInfo? resourceCulture;

        public static ResourceManager ResourceManager =>
            resourceMan ??= new ResourceManager("XRoadFolkWeb.Resources.ValidationMessages", typeof(ValidationMessages).Assembly);

        public static CultureInfo? Culture
        {
            get => resourceCulture;
            set => resourceCulture = value;
        }

        private static string Get(string name)
        {
            try { return ResourceManager.GetString(name, resourceCulture) ?? name; }
            catch (MissingManifestResourceException) { return name; }
        }

        // Existing keys
        public static string Ssn_Invalid => Get(nameof(Ssn_Invalid));
        public static string FirstName_MaxLength => Get(nameof(FirstName_MaxLength));
        public static string LastName_MaxLength => Get(nameof(LastName_MaxLength));
        public static string Dob_Format => Get(nameof(Dob_Format));
        public static string ProvideSsnOrNameDob => Get(nameof(ProvideSsnOrNameDob));
        public static string DobSsnMismatch => Get(nameof(DobSsnMismatch));

        // New keys required by attributes
        public static string Name_Invalid => Get(nameof(Name_Invalid));
        public static string FirstName_Invalid => Get(nameof(FirstName_Invalid));
        public static string LastName_Invalid => Get(nameof(LastName_Invalid));
    }
}