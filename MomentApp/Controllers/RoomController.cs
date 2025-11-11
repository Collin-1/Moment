using Microsoft.AspNetCore.Mvc;
using MomentApp.Models;
using MomentApp.Services;
using QRCoder;
using System.Drawing;
using System.Drawing.Imaging;

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
    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateRoomViewModel());
    }

    /// <summary>
    /// POST: Create a new room
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateRoomViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var expiry = TimeSpan.FromHours(model.ExpiryHours);
            var room = _roomService.CreateRoom(model.Name, expiry, model.RoomType);

            _logger.LogInformation($"Room created: {room.Id}");

            return RedirectToAction(nameof(Created), new { roomCode = room.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating room");
            ModelState.AddModelError("", "An error occurred while creating the room. Please try again.");
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
            ExpiresAt = room.ExpiresAt
        };

        return View(viewModel);
    }

    /// <summary>
    /// GET: Display join room form
    /// </summary>
    [HttpGet]
    public IActionResult Join(string? code)
    {
        var model = new JoinRoomViewModel();
        if (!string.IsNullOrEmpty(code))
        {
            model.RoomCode = code.ToUpper();
        }
        return View(model);
    }

    /// <summary>
    /// POST: Validate room code and proceed to display name selection
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Join(JoinRoomViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var roomCode = model.RoomCode.ToUpper();
        var room = _roomService.GetRoom(roomCode);

        if (room == null)
        {
            ModelState.AddModelError("RoomCode", "Room not found. Please check the code and try again.");
            return View(model);
        }

        // Check if room is at capacity
        var activeParticipants = room.Participants.Count(p => !p.HasLeft);
        if (activeParticipants >= room.MaxParticipants)
        {
            ModelState.AddModelError("RoomCode", "This room is at maximum capacity.");
            return View(model);
        }

        // Redirect to display name selection
        return RedirectToAction(nameof(SelectDisplay), new { roomCode });
    }

    /// <summary>
    /// GET: Select display name and color
    /// </summary>
    [HttpGet]
    public IActionResult SelectDisplay(string roomCode)
    {
        var room = _roomService.GetRoom(roomCode);
        if (room == null)
        {
            return NotFound();
        }

        // Get used colors
        var usedColors = room.Participants.Where(p => !p.HasLeft).Select(p => p.ColorHex).ToList();
        var availableColors = _colorService.GetAvailableColors(usedColors);

        // Suggest a random color
        var suggestedColor = availableColors.Any()
            ? availableColors.First().Value
            : _colorService.GetAllColors().First().Value;

        var model = new SelectDisplayViewModel
        {
            RoomCode = roomCode,
            ColorHex = suggestedColor
        };

        ViewBag.AvailableColors = availableColors;

        return View(model);
    }

    /// <summary>
    /// POST: Create participant and enter room
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SelectDisplay(SelectDisplayViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var room2 = _roomService.GetRoom(model.RoomCode);
            if (room2 != null)
            {
                var usedColors2 = room2.Participants.Where(p => !p.HasLeft).Select(p => p.ColorHex).ToList();
                ViewBag.AvailableColors = _colorService.GetAvailableColors(usedColors2);
            }
            return View(model);
        }

        var room = _roomService.GetRoom(model.RoomCode);
        if (room == null)
        {
            return NotFound();
        }

        // Create participant
        var participant = new Participant
        {
            DisplayName = model.DisplayName,
            ColorHex = model.ColorHex,
            JoinedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            Status = ParticipantStatus.Online
        };

        if (!_roomService.AddParticipant(model.RoomCode, participant))
        {
            ModelState.AddModelError("", "Failed to join room. Display name or color may already be in use.");
            var usedColors = room.Participants.Where(p => !p.HasLeft).Select(p => p.ColorHex).ToList();
            ViewBag.AvailableColors = _colorService.GetAvailableColors(usedColors);
            return View(model);
        }

        // Store participant ID in session
        HttpContext.Session.SetString($"ParticipantId_{model.RoomCode}", participant.Id);

        _logger.LogInformation($"Participant {participant.DisplayName} joined room {model.RoomCode}");

        // Redirect to chat room
        return RedirectToAction(nameof(Index), new { roomCode = model.RoomCode });
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
