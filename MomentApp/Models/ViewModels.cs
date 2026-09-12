using System.ComponentModel.DataAnnotations;

namespace MomentApp.Models;

/// <summary>
/// How a visitor will appear inside a room.
/// </summary>
/// <remarks>
/// Shared by both ways in. Asking for this on the same form that creates or finds the room
/// is what removes the separate "choose a display name" page: it was a whole round trip to
/// collect two fields that the form before it had room for.
/// </remarks>
public abstract class ParticipantEntryViewModel
{
    [Required(ErrorMessage = "Please enter a display name")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Display name must be between 2 and 20 characters")]
    // Excludes the characters that carry meaning in markup, plus control characters. Display
    // names are echoed into every other participant's page, so this keeps the value harmless
    // at the boundary as well as at each render site. Everything else, including non-Latin
    // scripts, is allowed.
    [RegularExpression(@"^[^<>&""'\\/\x00-\x1F\x7F]+$",
        ErrorMessage = "Display name cannot contain < > & \" ' \\ or /")]
    public string DisplayName { get; set; } = string.Empty;

    // Validated against ColorService's palette in the controller — an allow-list, because
    // this value ends up inside style attributes on other participants' pages.
    [Required(ErrorMessage = "Please choose a colour")]
    public string ColorHex { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel for creating a new room
/// </summary>
public class CreateRoomViewModel : ParticipantEntryViewModel
{
    /// <summary>
    /// Optional name for the room
    /// </summary>
    [StringLength(50, ErrorMessage = "Room name cannot exceed 50 characters")]
    public string? Name { get; set; }

    /// <summary>
    /// Optional description, shown to people as they join.
    /// </summary>
    [StringLength(140, ErrorMessage = "Description cannot exceed 140 characters")]
    public string? Description { get; set; }

    /// <summary>
    /// Minutes until the room expires.
    /// </summary>
    /// <remarks>
    /// Minutes rather than hours because the shortest offered room is 30 minutes, which an
    /// hour-based field could not express — the landing page was advertising short rooms the
    /// app had no way to create.
    /// </remarks>
    [Required(ErrorMessage = "Please choose how long this moment should last")]
    [Range(RoomDuration.MinMinutes, RoomDuration.MaxMinutes,
        ErrorMessage = "A moment can last between 5 minutes and 7 days")]
    public int ExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// Which surface the room opens into.
    /// </summary>
    /// <remarks>
    /// No longer asked for on the form. Every room carries text, voice and video whatever
    /// this says, and now that all three are reachable from the room's own header, the
    /// question bought a decision and changed nothing. It survives as the landing page's
    /// <c>?type=</c> hint, so "start a video call" still opens on the video surface.
    /// </remarks>
    [Required]
    public RoomType RoomType { get; set; } = RoomType.Chat;
}

/// <summary>
/// ViewModel for joining a room: the code and how you will appear, on one form.
/// </summary>
public class JoinRoomViewModel : ParticipantEntryViewModel
{
    [Required(ErrorMessage = "Please enter a room code")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Room code must be exactly 6 characters")]
    [RegularExpression("^[A-Z0-9]{6}$", ErrorMessage = "Room code must contain only uppercase letters and numbers")]
    public string RoomCode { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel for the chat room
/// </summary>
public class ChatRoomViewModel
{
    public Room Room { get; set; } = new Room();
    public Participant CurrentParticipant { get; set; } = new Participant();
    public List<string> AvailableColors { get; set; } = new List<string>();

    /// <summary>The absolute join link, for the in-room invite panel.</summary>
    public string ShareLink { get; set; } = string.Empty;
}
