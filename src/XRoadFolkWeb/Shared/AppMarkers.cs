namespace XRoadFolkWeb;

// Marker class for shared localization resources (layout, nav, etc.)
public sealed class SharedResource { }

public record LogWriteDto(string? Message, string? Category, string? Level, int? EventId);
