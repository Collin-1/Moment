# Moment - Real-time Ephemeral Messaging Platform

**Conversations that disappear**

Moment is a privacy-focused, temporary chat application built with .NET 9, SignalR, and MVC architecture. It creates spaces for discussions that naturally expire - either through time or by group consensus.

## 🌟 Features

### Core Functionality

- **No Accounts Required** - Jump right in, no signup needed
- **Truly Ephemeral** - Messages disappear forever when rooms expire
- **Real-time Communication** - Instant messaging powered by SignalR
- **Democratic Room Closure** - Groups vote together to end conversations
- **Privacy by Design** - No message logs, no tracking, no permanent storage

### Key Features

- ✨ **6-Character Room Codes** - Easy to share and remember
- 📱 **QR Code Sharing** - Quick mobile access
- ⏱️ **Flexible Expiry Times** - 1 hour to 7 days
- 🎨 **Color-Coded Identities** - 12 distinct colors for participants
- 💬 **System Messages** - Join/leave notifications
- 🗳️ **Majority Voting** - Democratic room closure with grace period
- 👥 **Participant Presence** - Online, Away, Offline status tracking
- 🔔 **Expiry Warnings** - 5-minute and 1-minute notifications
- ⚡ **Auto-Cleanup** - Inactive participants automatically removed

## 🏗️ Architecture

### Technology Stack

- **.NET 9** - Latest ASP.NET Core framework
- **SignalR** - Real-time bidirectional communication
- **MVC Pattern** - Clean separation of concerns
- **In-Memory Storage** - Truly ephemeral data (no database)
- **QRCoder** - QR code generation for easy sharing

### Project Structure

```
MomentApp/
├── Controllers/          # MVC controllers (HomeController, RoomController)
├── Views/               # Razor views for UI
│   ├── Home/           # Landing page
│   └── Room/           # Room creation, join, and chat views
├── Models/              # Data models and ViewModels
│   ├── Room.cs         # Room entity
│   ├── Participant.cs  # Participant entity
│   ├── Message.cs      # Message entity
│   ├── VoteSession.cs  # Voting session entity
│   └── ViewModels.cs   # View models for MVC
├── Services/            # Business logic layer
│   ├── RoomService.cs       # Room management
│   ├── MessageService.cs    # Message handling
│   ├── VotingService.cs     # Voting logic
│   ├── TimerService.cs      # Background timer/expiry
│   └── ColorService.cs      # Color palette management
├── Hubs/                # SignalR hubs
│   ├── ChatHub.cs      # Real-time messaging
│   └── RoomHub.cs      # Participant/room management
└── wwwroot/             # Static files

```

## 🚀 Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A modern web browser (Chrome, Firefox, Safari, Edge)

### Installation & Running

1. **Clone or navigate to the project**

   ```bash
   cd MomentApp
   ```

2. **Restore dependencies**

   ```bash
   dotnet restore
   ```

3. **Build the project**

   ```bash
   dotnet build
   ```

4. **Run the application**

   ```bash
   dotnet run
   ```

5. **Open in browser**
   - Navigate to `http://localhost:5173` (or the URL shown in terminal)

## 📖 How to Use

### Creating a Room

1. Click "Create New Moment" on the landing page
2. Optionally name your room
3. Choose expiry time (1 hour to 7 days)
4. Select room type (Group or 1-on-1)
5. Share the generated room code or QR code

### Joining a Room

1. Click "Join a Moment"
2. Enter the 6-character room code
3. Choose a display name and color
4. Start chatting!

### Chatting

- Type messages in the input box at the bottom
- Messages appear instantly for all participants
- See who's typing in real-time
- View participant status (online/away/offline)
- Watch the countdown timer

### Voting to Close

1. Any participant can initiate a vote
2. All participants see the vote panel
3. Cast your vote (Yes or No)
4. When >50% vote yes, a 5-minute grace period starts
5. Room closes after grace period

### Room Expiry

- Automatic warnings at 5 minutes and 1 minute
- Room and all messages deleted on expiry
- Users redirected to "Room Closed" page

## 🔒 Privacy & Security

### Privacy Guarantees

- ✅ No user accounts or authentication
- ✅ No message logging beyond room lifetime
- ✅ No analytics or tracking
- ✅ Anonymous by default
- ✅ Complete data deletion on room closure

### Security Features

- **Rate Limiting** - 10 messages per minute per user
- **Input Sanitization** - XSS protection via HTML encoding
- **CSRF Protection** - Anti-forgery tokens on all forms
- **Content Validation** - Max 2000 characters per message
- **Collision-Safe Codes** - Unique room code generation

## 🎨 UI/UX Features

### Responsive Design

- Mobile-first approach
- Adapts to all screen sizes
- Touch-friendly interface

### Real-time Updates

- Instant message delivery
- Live timer countdown
- Dynamic participant list
- Typing indicators
- Connection status indicators

### Visual Feedback

- Color-coded messages
- Status dots (green/yellow/red)
- Timer color changes (normal/yellow/red)
- Notification toasts
- Reconnecting banners

## 🧪 Testing

To test the application:

1. **Single User Test**

   - Create a room
   - Verify QR code generation
   - Check timer display

2. **Multi-User Test**

   - Open room in two browser windows/tabs
   - Test real-time messaging
   - Verify participant presence
   - Test voting system

3. **Timer Test**
   - Create a 1-hour room
   - Wait for expiry warnings (or adjust timer for testing)
   - Verify automatic room closure

## 📝 Code Highlights

### SignalR Hubs

- **ChatHub** - Handles real-time messaging with rate limiting
- **RoomHub** - Manages participants, voting, and room state

### Background Services

- **TimerService** - Checks room expiry every 10 seconds
- Auto-cleanup of inactive participants
- Broadcasts timer updates to all clients

### Service Layer

- **RoomService** - Thread-safe in-memory room storage using `ConcurrentDictionary`
- **MessageService** - Message validation and XSS sanitization
- **VotingService** - Dynamic majority calculation
- **ColorService** - 12-color palette management

## 🛠️ Development

### Key Technologies Used

- **ASP.NET Core 9.0** - Web framework
- **SignalR** - WebSocket communication
- **Razor Pages** - Server-side rendering
- **QRCoder** - QR code generation
- **Sessions** - Participant tracking

### Design Patterns

- **Repository Pattern** - In-memory data storage
- **Service Layer** - Business logic separation
- **MVC Pattern** - Clean architecture
- **Background Service** - Timer management

## 📋 Roadmap Features (Not Yet Implemented)

Future enhancements could include:

- File/image sharing
- Emoji reactions
- Message search
- Room password protection
- Multiple room types (voice, video)
- Persistent storage option
- Analytics dashboard
- Mobile apps (Xamarin/MAUI)

## ⚠️ Limitations

- **In-Memory Storage** - Rooms lost on server restart (by design)
- **No Persistence** - Cannot recover closed rooms
- **Max 50 Participants** - Performance limit per room
- **No File Upload** - Text messages only
- **Single Server** - No load balancing (scalability limited)

## 🤝 Contributing

This is a portfolio project demonstrating:

- Real-time communication with SignalR
- Clean MVC architecture
- Background services in .NET
- Privacy-focused design
- Responsive UI/UX

## 📄 License

This project is created for educational and portfolio purposes.

## 👤 Author

Created as a portfolio project to demonstrate:

- Advanced SignalR usage
- Real-time system design
- .NET 9 best practices
- Privacy-first development

## 🎯 Success Criteria Met

✅ Real-time messaging works flawlessly  
✅ Multiple users can chat simultaneously  
✅ Timer system is accurate  
✅ Voting system handles all scenarios  
✅ No data persists after room closes  
✅ Clean, professional UI  
✅ Mobile responsive  
✅ Secure input handling

---

**Built with ❤️ using .NET 9 and SignalR**

For questions or feedback about this portfolio project, please reach out!
