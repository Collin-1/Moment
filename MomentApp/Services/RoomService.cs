using System.Collections.Concurrent;
using System.Security.Cryptography;
using MomentApp.Models;

namespace MomentApp.Services;

/// <summary>
/// In-memory implementation of room management service
/// </summary>
public class RoomService : IRoomService
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);

    // Excludes I, O, 0 and 1 — characters people misread when typing a code from a screen.
    private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public Room CreateRoom(string? name, string? description, TimeSpan expiry, RoomType type)
    {
        var room = new Room
        {
            Id = GenerateUniqueCode(),
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(expiry),
            Type = type
        };

        if (!_rooms.TryAdd(room.Id, room))
        {
            throw new InvalidOperationException("Failed to create room");
        }

        return room;
    }

    public Room? GetRoom(string roomId)
    {
        if (string.IsNullOrEmpty(roomId))
        {
            return null;
        }

        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public void DeleteRoom(string roomId)
    {
        _rooms.TryRemove(roomId, out _);
    }

    public string GenerateUniqueCode()
    {
        const int maxAttempts = 100;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var code = GenerateCode();
            if (!_rooms.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Unable to generate unique room code after multiple attempts");
    }

    /// <summary>
    /// Generates a room code using a cryptographic RNG.
    /// </summary>
    /// <remarks>
    /// The room code is this app's entire access control — there are no accounts and no
    /// other credential. A predictable code is a guessable room, so this must not use
    /// <see cref="Random"/>. (A shared <c>Random</c> instance was also not thread-safe:
    /// concurrent use can corrupt its internal state into returning zeros indefinitely.)
    /// </remarks>
    private static string GenerateCode() =>
        new(RandomNumberGenerator.GetItems<char>(CodeCharacters, 6));

    public bool AddParticipant(string roomId, Participant participant)
    {
        var room = GetRoom(roomId);
        if (room == null)
        {
            return false;
        }

        // Capacity and uniqueness are a compound check-then-act: without the lock, two
        // simultaneous joins can both observe a free slot, or both claim the same colour.
        lock (room.MutationLock)
        {
            var active = room.Participants.Where(p => !p.HasLeft).ToArray();

            if (active.Length >= room.MaxParticipants)
            {
                return false;
            }

            if (active.Any(p =>
                    p.DisplayName.Equals(participant.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                    p.ColorHex.Equals(participant.ColorHex, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return room.TryAddParticipant(participant);
        }
    }

    public void RemoveParticipant(string roomId, string participantId)
    {
        var room = GetRoom(roomId);
        if (room == null) return;

        lock (room.MutationLock)
        {
            var participant = room.FindParticipant(participantId);
            if (participant != null)
            {
                participant.HasLeft = true;
                participant.Status = ParticipantStatus.Offline;
            }

            // Delete the room once nobody is left in it.
            if (room.Participants.All(p => p.HasLeft))
            {
                DeleteRoom(roomId);
            }
        }
    }

    public void UpdateParticipantStatus(string roomId, string participantId, ParticipantStatus status)
    {
        var participant = GetRoom(roomId)?.FindParticipant(participantId);
        if (participant != null)
        {
            participant.Status = status;
        }
    }

    public IEnumerable<Room> GetAllRooms()
    {
        return _rooms.Values;
    }

    public void UpdateParticipantActivity(string roomId, string participantId)
    {
        var participant = GetRoom(roomId)?.FindParticipant(participantId);
        if (participant == null) return;

        participant.LastActivity = DateTime.UtcNow;
        if (participant.Status != ParticipantStatus.Online)
        {
            participant.Status = ParticipantStatus.Online;
        }
    }

    public Participant? GetParticipant(string roomId, string participantId)
    {
        return GetRoom(roomId)?.FindParticipant(participantId);
    }

    public Participant? GetParticipantByConnectionId(string roomId, string connectionId)
    {
        var room = GetRoom(roomId);
        return room?.Participants.FirstOrDefault(p => p.ConnectionId == connectionId);
    }

    public void StartGracePeriod(string roomId)
    {
        var room = GetRoom(roomId);
        if (room == null) return;

        room.IsInGracePeriod = true;
        room.GracePeriodStartedAt = DateTime.UtcNow;
        room.ExpiresAt = DateTime.UtcNow.AddMinutes(5); // 5-minute grace period
    }
}
