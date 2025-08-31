namespace XRoadFolkWeb.Shared
{
    /// <summary>
    /// Marker class for shared localization resources (layout, nav, etc.)
    /// </summary>
    public sealed class SharedResource;

    public record LogWriteDto(string? Message, string? Category, string? Level, int? EventId);
}
