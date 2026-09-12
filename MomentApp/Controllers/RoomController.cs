using Microsoft.AspNetCore.Mvc;
using MomentApp.Models;
using MomentApp.Services;
using QRCoder;

namespace MomentApp.Controllers;

/// <summary>
/// Controller for room management operations
/// </summary>
public class RoomController : Controller
{
    private readonly IRoomService _roomService;
    private readonly IMessageService _messageService;
    private readonly ColorService _colorService;
    private readonly ILogger<RoomController> _logger;

    public RoomController(
        IRoomService roomService,
        IMessageService messageService,
        ColorService colorService,
        ILogger<RoomController> logger)
    {
        _roomService = roomService;
        _messageService = messageService;
        _colorService = colorService;
        _logger = logger;
    }

    /// <summary>
    /// GET: Display create room form
    /// </summary>
    /// <param name="type">
    /// Which surface the new room opens into, carried from the landing page's two buttons.
    /// Not asked for on the form: every room supports all three regardless.
    /// </param>
    [HttpGet]
    public IActionResult Create(RoomType? type)
    {
        var colors = _colorService.GetAllColors();
        ViewBag.AvailableColors = colors;

        return View(new CreateRoomViewModel
        {
            RoomType = type ?? RoomType.Chat,
            ColorHex = colors.First().Value
        });
    }

    /// <summary>
    /// POST: Create a new room and put its creator inside it.
    /// </summary>
    /// <remarks>
    /// The participant is created here, not on a later page. Without it the creator arrived at
    /// their own room with no session entry, <see cref="Index"/> read that as a stranger, and
    /// bounced them into the join-a-stranger's-room flow — for a code they had just been given,
    /// through a form that was a formality they still had to submit.
    /// </remarks>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateRoomViewModel model)
    {
        if (!_colorService.IsKnownColor(model.ColorHex))
        {
            ModelState.AddModelError(nameof(model.ColorHex), "Please choose one of the available colours.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.AvailableColors = _colorService.GetAllColors();
            return View(model);
        }

        try
        {
            var expiry = TimeSpan.FromMinutes(model.ExpiryMinutes);
            var room = _roomService.CreateRoom(model.Name, model.Description, expiry, model.RoomType);

            var participant = NewParticipant(model.DisplayName, model.ColorHex);

            // A brand new room is empty, so neither the capacity nor the uniqueness check can
            // fail. If it somehow does, the room exists and is still usable — send them
            // through the join form rather than losing it.
            if (!_roomService.AddParticipant(room.Id, participant))
            {
                _logger.LogWarning("Creator could not be added to new room {RoomId}", room.Id);
                return RedirectToAction(nameof(Join), new { code = room.Id });
            }

            HttpContext.Session.SetString($"ParticipantId_{room.Id}", participant.Id);

            _logger.LogInformation("Room created: {RoomId}", room.Id);

            return RedirectToAction(nameof(Created), new { roomCode = room.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating room");
            ModelState.AddModelError("", "An error occurred while creating the room. Please try again.");
            ViewBag.AvailableColors = _colorService.GetAllColors();
            return View(model);
        }
    }

    /// <summary>
    /// Display room created success page with QR code
    /// </summary>
    public IActionResult Created(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            return NotFound();
        }

        var shareableLink = Url.Action("Join", "Room", new { code = roomCode }, Request.Scheme);
        var qrCodeDataUrl = GenerateQRCode(shareableLink!);

        var viewModel = new RoomCreatedViewModel
        {
            RoomCode = room.Id,
            RoomName = room.Name,
            ShareableLink = shareableLink!,
            QRCodeDataUrl = qrCodeDataUrl,
            ExpiresAt = room.ExpiresAt,
            ExpiryMinutes = (int)Math.Round((room.ExpiresAt - room.CreatedAt).TotalMinutes),
            RoomType = room.Type
        };

        return View(viewModel);
    }

    /// <summary>
    /// GET: the one form that takes somebody from a link or a code to inside the room.
    /// </summary>
    /// <remarks>
    /// Code, display name and colour together. These used to be two pages: one that checked
    /// the code and immediately redirected, and one that collected two fields. The check the
    /// first page performed has to be repeated on the second anyway — a room can fill up
    /// between them — so the extra round trip bought nothing.
    /// </remarks>
    [HttpGet]
    public IActionResult Join(string? code)
    {
        var roomCode = string.IsNullOrEmpty(code) ? string.Empty : code.ToUpperInvariant();
        var colors = ColorsFor(roomCode);
        ViewBag.AvailableColors = colors;

        return View(new JoinRoomViewModel
        {
            RoomCode = roomCode,
            ColorHex = colors.Count > 0 ? colors.First().Value : string.Empty
        });
    }

    /// <summary>
    /// POST: Validate the code, create the participant and enter the room.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Join(JoinRoomViewModel model)
    {
        var roomCode = (model.RoomCode ?? string.Empty).ToUpperInvariant();
        model.RoomCode = roomCode;

        // The chosen colour is rendered into every other participant's page, including into
        // style attributes. Accepting an arbitrary string here would be a stored-XSS vector,
        // so it is checked against the fixed palette rather than trusted or pattern-matched.
        if (!_colorService.IsKnownColor(model.ColorHex))
        {
            ModelState.AddModelError(nameof(model.ColorHex), "Please choose one of the available colours.");
        }

        if (!ModelState.IsValid)
        {
            return JoinAgain(model);
        }

        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            ModelState.AddModelError(nameof(model.RoomCode), "Room not found. Please check the code and try again.");
            return JoinAgain(model);
        }

        if (room.Participants.Count(p => !p.HasLeft) >= room.MaxParticipants)
        {
            ModelState.AddModelError(nameof(model.RoomCode), "This room is at maximum capacity.");
            return JoinAgain(model);
        }

        var participant = NewParticipant(model.DisplayName, model.ColorHex);

        // AddParticipant re-checks capacity and uniqueness under the room's lock, so this is
        // the decision that counts; the checks above only produce a better message.
        if (!_roomService.AddParticipant(roomCode, participant))
        {
            ModelState.AddModelError("", "That name or colour is already taken in this room.");
            return JoinAgain(model);
        }

        HttpContext.Session.SetString($"ParticipantId_{roomCode}", participant.Id);

        _logger.LogInformation("Participant {ParticipantId} joined room {RoomId}", participant.Id, roomCode);

        return RedirectToAction(nameof(Index), new { roomCode });
    }

    /// <summary>
    /// Display chat room
    /// </summary>
    public IActionResult Index(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            return NotFound();
        }

        // Get current participant from session
        var participantId = HttpContext.Session.GetString($"ParticipantId_{roomCode}");
        if (string.IsNullOrEmpty(participantId))
        {
            // Redirect to join flow
            return RedirectToAction(nameof(Join), new { code = roomCode });
        }

        var participant = room.Participants.FirstOrDefault(p => p.Id == participantId);
        if (participant == null || participant.HasLeft)
        {
            // Participant not found or has left
            HttpContext.Session.Remove($"ParticipantId_{roomCode}");
            return RedirectToAction(nameof(Join), new { code = roomCode });
        }

        var viewModel = new ChatRoomViewModel
        {
            Room = room,
            CurrentParticipant = participant,
            AvailableColors = _colorService.GetAllColors().Keys.ToList()
        };

        return View(viewModel);
    }

    /// <summary>
    /// Room closed page
    /// </summary>
    public IActionResult Closed()
    {
        return View();
    }

    /// <summary>
    /// The colours still free in a room, or the whole palette when the room is unknown.
    /// </summary>
    /// <remarks>
    /// Somebody typing a code by hand has not told us which room they mean yet, so the form
    /// offers everything and the post sorts it out. Arriving by link, the room is known and
    /// the taken colours are simply absent — a colour clash is then rare rather than routine.
    /// </remarks>
    private Dictionary<string, string> ColorsFor(string? roomCode)
    {
        var room = string.IsNullOrEmpty(roomCode) ? null : _roomService.GetRoom(roomCode);
        if (room == null)
        {
            return _colorService.GetAllColors();
        }

        var used = room.Participants.Where(p => !p.HasLeft).Select(p => p.ColorHex).ToList();
        var available = _colorService.GetAvailableColors(used);
        return available.Count > 0 ? available : _colorService.GetAllColors();
    }

    /// <summary>Re-renders the join form with its swatches intact after a failed post.</summary>
    private IActionResult JoinAgain(JoinRoomViewModel model)
    {
        ViewBag.AvailableColors = ColorsFor(model.RoomCode);
        return View(nameof(Join), model);
    }

    private static Participant NewParticipant(string displayName, string colorHex) => new()
    {
        DisplayName = displayName,
        ColorHex = colorHex,
        JoinedAt = DateTime.UtcNow,
        LastActivity = DateTime.UtcNow,
        Status = ParticipantStatus.Online
    };

    /// <summary>
    /// Generate QR code for shareable link
    /// </summary>
    private string GenerateQRCode(string url)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(20);
            return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating QR code");
            return string.Empty;
        }
    }
}
