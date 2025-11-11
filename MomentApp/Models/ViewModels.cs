using System.ComponentModel.DataAnnotations;

namespace MomentApp.Models;

/// <summary>
/// ViewModel for creating a new room
/// </summary>
public class CreateRoomViewModel
{
    /// <summary>
    /// Optional name for the room
    /// </summary>
    [StringLength(50, ErrorMessage = "Room name cannot exceed 50 characters")]
    public string? Name { get; set; }

    /// <summary>
    /// Duration in hours until the room expires
    /// </summary>
    [Required(ErrorMessage = "Please select an expiry time")]
    [Range(1, 168, ErrorMessage = "Expiry time must be between 1 and 168 hours")]
    public int ExpiryHours { get; set; } = 6;

    /// <summary>
    /// Type of room
    /// </summary>
    [Required]
    public RoomType RoomType { get; set; } = RoomType.Group;
}

/// <summary>
/// ViewModel for displaying room creation success
/// </summary>
public class RoomCreatedViewModel
{
    public string RoomCode { get; set; } = string.Empty;
    public string? RoomName { get; set; }
    public string ShareableLink { get; set; } = string.Empty;
    public string QRCodeDataUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// ViewModel for joining a room
/// </summary>
public class JoinRoomViewModel
{
    [Required(ErrorMessage = "Please enter a room code")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "Room code must be exactly 6 characters")]
    [RegularExpression("^[A-Z0-9]{6}$", ErrorMessage = "Room code must contain only uppercase letters and numbers")]
    public string RoomCode { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel for selecting participant display name and color
/// </summary>
public class SelectDisplayViewModel
{
    [Required(ErrorMessage = "Please enter a display name")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Display name must be between 2 and 20 characters")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a color")]
    public string ColorHex { get; set; } = string.Empty;

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
}
