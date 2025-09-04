namespace XRoadFolkWeb.Shared
{
    public record LogWriteDto(string? Message, string? Category, string? Level, int? EventId);
}
