using MomentApp.Models;

namespace MomentApp.Services;

/// <summary>
/// Interface for room management operations
/// </summary>
public interface IRoomService
{
    /// <summary>
    /// Creates a new room with the specified parameters
    /// </summary>
    Room CreateRoom(string? name, TimeSpan expiry, RoomType type);

    /// <summary>
    /// Gets a room by its code
    /// </summary>
    Room? GetRoom(string roomId);

    /// <summary>
    /// Deletes a room and all its data
    /// </summary>
    void DeleteRoom(string roomId);

    /// <summary>
    /// Generates a unique 6-character room code
    /// </summary>
    string GenerateUniqueCode();

    /// <summary>
    /// Adds a participant to a room
    /// </summary>
    bool AddParticipant(string roomId, Participant participant);

    /// <summary>
    /// Removes a participant from a room
    /// </summary>
    void RemoveParticipant(string roomId, string participantId);

    /// <summary>
    /// Updates a participant's status
    /// </summary>
    void UpdateParticipantStatus(string roomId, string participantId, ParticipantStatus status);

    /// <summary>
    /// Gets all active rooms
    /// </summary>
    IEnumerable<Room> GetAllRooms();

    /// <summary>
    /// Updates participant's last activity timestamp
    /// </summary>
    void UpdateParticipantActivity(string roomId, string participantId);

    /// <summary>
    /// Gets a participant by connection ID
    /// </summary>
    Participant? GetParticipantByConnectionId(string roomId, string connectionId);

    /// <summary>
    /// Starts grace period for a room
    /// </summary>
    void StartGracePeriod(string roomId);
}
