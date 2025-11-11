using System.Collections.Concurrent;
using MomentApp.Models;

namespace MomentApp.Services;

/// <summary>
/// In-memory implementation of room management service
/// </summary>
public class RoomService : IRoomService
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly Random _random = new();
    private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Excluding confusing characters

    public Room CreateRoom(string? name, TimeSpan expiry, RoomType type)
    {
        var room = new Room
        {
            Id = GenerateUniqueCode(),
            Name = name,
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
        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public void DeleteRoom(string roomId)
    {
        _rooms.TryRemove(roomId, out _);
    }

    public string GenerateUniqueCode()
    {
        string code;
        int attempts = 0;
        const int maxAttempts = 100;

        do
        {
            code = GenerateCode();
            attempts++;

            if (attempts > maxAttempts)
            {
                throw new InvalidOperationException("Unable to generate unique room code after multiple attempts");
            }
        } while (_rooms.ContainsKey(code));

        return code;
    }

    private string GenerateCode()
    {
        var chars = new char[6];
        for (int i = 0; i < 6; i++)
        {
            chars[i] = CodeCharacters[_random.Next(CodeCharacters.Length)];
        }
        return new string(chars);
    }

    public bool AddParticipant(string roomId, Participant participant)
    {
        var room = GetRoom(roomId);
        if (room == null)
        {
            return false;
        }

        // Check if room is at capacity
        if (room.Participants.Count(p => !p.HasLeft) >= room.MaxParticipants)
        {
            return false;
        }

        // Check for duplicate display name or color
        if (room.Participants.Any(p => !p.HasLeft &&
            (p.DisplayName.Equals(participant.DisplayName, StringComparison.OrdinalIgnoreCase) ||
             p.ColorHex.Equals(participant.ColorHex, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        room.Participants.Add(participant);
        return true;
    }

    public void RemoveParticipant(string roomId, string participantId)
    {
        var room = GetRoom(roomId);
        if (room == null) return;

        var participant = room.Participants.FirstOrDefault(p => p.Id == participantId);
        if (participant != null)
        {
            participant.HasLeft = true;
            participant.Status = ParticipantStatus.Offline;
        }

        // Delete room if no active participants
        if (room.Participants.All(p => p.HasLeft))
        {
            DeleteRoom(roomId);
        }
    }

    public void UpdateParticipantStatus(string roomId, string participantId, ParticipantStatus status)
    {
        var room = GetRoom(roomId);
        if (room == null) return;

        var participant = room.Participants.FirstOrDefault(p => p.Id == participantId);
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
        var room = GetRoom(roomId);
        if (room == null) return;

        var participant = room.Participants.FirstOrDefault(p => p.Id == participantId);
        if (participant != null)
        {
            participant.LastActivity = DateTime.UtcNow;
            if (participant.Status != ParticipantStatus.Online)
            {
                participant.Status = ParticipantStatus.Online;
            }
        }
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
