namespace ClipboardManager.Domain;

/// <summary>Composable filters, evaluated in SQLite before pagination.</summary>
public sealed record ClipboardQuery
{
    public string Text { get; init; } = string.Empty;
    public ClipboardType? Type { get; init; }
    public string? Source { get; init; }
    public DateTime? Since { get; init; }
    public string? Group { get; init; }
    public bool FavoritesOnly { get; init; }
}

public sealed class Pinboard
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#7C6AF2";
    public int Position { get; set; }
}
