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
        if (!availableColors.Any())
        {
            // If all colors are taken, return a random one anyway
            var random = new Random();
            return AvailableColors.ElementAt(random.Next(AvailableColors.Count));
        }

        var random2 = new Random();
        return availableColors.ElementAt(random2.Next(availableColors.Count));
    }

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
