namespace MomentApp.Services;

/// <summary>
/// Service for managing participant color palette
/// </summary>
public class ColorService
{
    private static readonly Dictionary<string, string> AvailableColors = new()
    {
        { "Red", "#EF4444" },
        { "Orange", "#F97316" },
        { "Amber", "#F59E0B" },
        { "Green", "#10B981" },
        { "Teal", "#14B8A6" },
        { "Blue", "#3B82F6" },
        { "Indigo", "#6366F1" },
        { "Purple", "#A855F7" },
        { "Pink", "#EC4899" },
        { "Rose", "#F43F5E" },
        { "Cyan", "#06B6D4" },
        { "Lime", "#84CC16" }
    };

    /// <summary>
    /// Gets all available colors
    /// </summary>
    public Dictionary<string, string> GetAllColors()
    {
        return AvailableColors;
    }

    /// <summary>
    /// Gets colors not currently in use in a room
    /// </summary>
    public Dictionary<string, string> GetAvailableColors(IEnumerable<string> usedColors)
    {
        var usedColorSet = usedColors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return AvailableColors
            .Where(c => !usedColorSet.Contains(c.Value))
            .ToDictionary(c => c.Key, c => c.Value);
    }

    /// <summary>
    /// Gets a random available color
    /// </summary>
    public KeyValuePair<string, string> GetRandomAvailableColor(IEnumerable<string> usedColors)
    {
        var availableColors = GetAvailableColors(usedColors);

        // If every color is taken, hand back any of them rather than failing the join.
        var pool = availableColors.Count > 0 ? availableColors : AvailableColors;
        return pool.ElementAt(Random.Shared.Next(pool.Count));
    }

    /// <summary>
    /// Whether a hex code is one this app actually issues.
    /// </summary>
    /// <remarks>
    /// The colour a participant picks is echoed into every other participant's page — into a
    /// <c>style</c> attribute, among other places. Accepting an arbitrary string there is a
    /// stored-XSS vector, so the palette is treated as an allow-list rather than validated by
    /// pattern. The palette is fixed and small, which makes this the cheapest correct check.
    /// </remarks>
    public bool IsKnownColor(string? hexCode) =>
        !string.IsNullOrEmpty(hexCode) &&
        AvailableColors.Values.Contains(hexCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Number of distinct colors available, which is the real ceiling on room size.
    /// </summary>
    public int PaletteSize => AvailableColors.Count;

    /// <summary>
    /// Gets color name from hex code
    /// </summary>
    public string? GetColorName(string hexCode)
    {
        var color = AvailableColors.FirstOrDefault(c =>
            c.Value.Equals(hexCode, StringComparison.OrdinalIgnoreCase));
        return color.Key;
    }
}
