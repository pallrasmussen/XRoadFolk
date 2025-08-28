// Strongly-typed accessor used by DataAnnotations DisplayAttribute
#nullable enable annotations
namespace XRoadFolkWeb.Resources
{
    using System.Globalization;
    using System.Resources;

    public static class Labels
    {
        private static ResourceManager? _rm;
        private static CultureInfo? _culture;

        public static ResourceManager ResourceManager =>
            _rm ??= new ResourceManager("XRoadFolkWeb.Resources.Labels", typeof(Labels).Assembly);

        public static CultureInfo? Culture
        {
            get => _culture;
            set => _culture = value;
        }

        private static string Get(string name)
        {
            try { return ResourceManager.GetString(name, _culture) ?? name; }
            catch (MissingManifestResourceException) { return name; }
        }

        public static string SSN => Get(nameof(SSN));
        public static string FirstName => Get(nameof(FirstName));
        public static string LastName => Get(nameof(LastName));
        public static string DateOfBirth => Get(nameof(DateOfBirth));
    }
}