namespace MomentApp.Models;

/// <summary>
/// The room lifetimes offered on the create screen.
/// </summary>
/// <remarks>
/// Held in minutes rather than hours. The design offers a 30-minute room, which the previous
/// hour-based model could not express — and the landing page was advertising short rooms the
/// app could not actually create.
/// </remarks>
public static class RoomDuration
{
    public const int MinMinutes = 5;
    public const int MaxMinutes = 7 * 24 * 60; // a week is the outer bound

    /// <summary>Preset lifetimes, in minutes, in the order the design lists them.</summary>
    public static readonly IReadOnlyList<(int Minutes, string Label)> Presets = new[]
    {
        (30, "30 minutes"),
        (60, "1 hour"),
        (180, "3 hours"),
        (1440, "24 hours"),
        (4320, "3 days"),
    };

    /// <summary>Renders a duration the way a person would say it.</summary>
    public static string Describe(int minutes) => minutes switch
    {
        < 60 => $"{minutes} minutes",
        < 120 => "1 hour",
        < 1440 when minutes % 60 == 0 => $"{minutes / 60} hours",
        < 1440 => $"{minutes / 60}h {minutes % 60}m",
        < 2880 => "1 day",
        _ when minutes % 1440 == 0 => $"{minutes / 1440} days",
        _ => $"{minutes / 1440}d {(minutes % 1440) / 60}h",
    };
}
