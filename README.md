# Moment

Moment is a privacy-first, real-time ephemeral chat platform built with ASP.NET Core MVC, SignalR, and in-memory storage. Rooms are temporary by design: conversations expire after a timer or close by group vote, and room state is not persisted to a database.

## Overview

Moment is designed for conversations that should exist only for a short period of time. The app supports room creation, code-based joining, real-time messaging, participant presence, room expiry, and democratic room closure.

The current application flow is:

- Home page at the MVC landing page
- Create room at `/Room/Create`
- Join room at `/Room/Join`
- Select display name and color at `/Room/SelectDisplay`
- Live chat room at `/Room/Index`
- Closed room state at `/Room/Closed`

## Key Features

- No account required
- 6-character room codes
- Optional room names
- Expiry windows from 1 hour to 7 days
- Group chat or 1-on-1 room types
- QR code generation for sharing room links
- Real-time messaging with SignalR
- Participant presence and status updates
- Typing indicators and reconnect handling
- Voice and video calling (opt-in, up to 10 participants)
- Camera toggle during calls
- Vote-to-close room workflow
- In-memory storage for temporary room data
- Privacy-focused UX with no permanent chat history

## Technology Stack

- .NET 9 / ASP.NET Core MVC
- SignalR for live updates
- Razor views for server-rendered pages
- QRCoder for room QR codes
- Session state for participant tracking
- In-memory services for rooms, messages, votes, timers, and colors

## Project Structure

```text
MomentApp/
├── Controllers/   # HomeController, RoomController
├── Hubs/          # ChatHub, RoomHub
├── Models/        # Rooms, participants, messages, view models
├── Services/      # Room, message, voting, timer, and color services
├── Views/         # Home and room Razor views
├── wwwroot/       # CSS, JavaScript, images, and static assets
└── Program.cs     # App startup, DI, SignalR, sessions, routes
```

## How It Works

### Room creation

Users open the create form, optionally name a room, choose an expiry duration, and pick a room type. The app creates a 6-character code, generates a QR link, and stores the room in memory.

### Joining a room

Users enter a code, choose a display name and color, and then enter the live room. The chosen participant identity is stored in session so they can return to the same room state.

### Chat and presence

The chat room uses SignalR hubs to broadcast messages, participant joins and leaves, typing state, timer updates, and voting activity in real time.

### Voice and video

Participants can join an opt-in group call (up to 10 users). Calls use WebRTC for audio/video streams and SignalR for signaling. Users can toggle the camera on or off without leaving the call.

### Room closure

Rooms can end naturally when the timer expires or earlier through a majority vote. When a room closes, its in-memory state is removed.

## Local Development

### Prerequisites

- .NET 9 SDK
- A modern browser such as Chrome, Edge, Firefox, or Safari

### Run locally

```bash
dotnet restore
dotnet build
dotnet run
```

The app will start on the port shown in the terminal. If you want to use a fixed port during development, you can run:

```bash
dotnet run --urls "http://localhost:5000"
```

## Configuration Notes

- Static files are served from `wwwroot/`
- SignalR hubs are mapped at `/hubs/chat` and `/hubs/room`
- Session state is enabled for participant tracking
- The app uses in-memory services, so room state is lost when the process restarts

## UI Notes

- The landing page uses a custom hero layout and full-page marketing sections
- Room creation and join screens use the shared theme in `wwwroot/css/theme.css`
- The live chat room uses a separate full-screen layout with a dark, high-contrast interface

## Testing Checklist

- Create a room and confirm the room code is generated
- Join the same room from a second browser window
- Send messages and confirm real-time delivery
- Verify participant presence updates
- Trigger a vote-to-close flow
- Confirm the timer counts down and room closure works

## Deployment

The repository includes deployment support files for container-based hosting:

- `Dockerfile`
- `render.yaml`
- `.dockerignore`

## Limitations

- Room data is not persisted between restarts
- Closed rooms cannot be recovered
- The app is designed for temporary collaboration rather than archival chat

## License

This project is intended for portfolio and demonstration purposes.

## Author

Moment was built to demonstrate real-time communication, privacy-first architecture, and modern ASP.NET Core application design.
