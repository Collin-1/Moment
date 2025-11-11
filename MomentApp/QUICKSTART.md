# 🚀 Quick Start Guide - Moment App

## First Time Setup

### 1. Prerequisites Check

Make sure you have .NET 9 installed:

```bash
dotnet --version
```

Should show `9.0.x`

### 2. Navigate to Project

```bash
cd MomentApp
```

### 3. Run the Application

```bash
dotnet run
```

The app will start at `http://localhost:5173`

## Testing the App (Single User)

1. **Open your browser** to `http://localhost:5173`
2. **Click "Create New Moment"**
3. Fill in the form:
   - Name: "Test Room" (optional)
   - Expiry: 1 hour
   - Type: Group Chat
4. **Click "Create Room"**
5. You'll see:
   - Your room code (e.g., "XY4K2M")
   - QR code
   - Share buttons
6. **Click "Enter Room"**
7. Choose:
   - Display name: "Alice"
   - Color: Blue
8. **Click "Join Room"**
9. You're in the chat! Try:
   - Typing a message
   - Watching the timer count down

## Testing Multi-User Chat

### Option 1: Multiple Browser Windows

1. Keep the first window open in the chat room
2. **Copy the room code** (from sidebar)
3. **Open a new incognito/private window**
4. Go to `http://localhost:5173`
5. **Click "Join a Moment"**
6. Enter the room code
7. Choose different name/color (e.g., "Bob", Red)
8. **Start chatting!**
9. Watch messages appear in both windows in real-time

### Option 2: Multiple Browsers

1. Use Chrome for first user
2. Use Firefox/Edge for second user
3. Both join the same room code
4. Chat away!

## Testing Key Features

### ✅ Real-time Messaging

- Type in one window
- See it appear instantly in the other
- Notice typing indicators

### ✅ Participant Presence

- Close one browser window
- Wait 5 seconds
- See status change to "Offline" in other window

### ✅ Voting System

1. Have at least 2 users in a room
2. Click "Vote to Close Room" button
3. Vote panel appears for all users
4. Cast votes (Yes/No)
5. When majority reached, 5-minute grace period starts

### ✅ Timer System

- Watch timer count down
- For quick testing, create a 1-hour room
- Timer shows in format: "0h 59m" or "59m 30s"

### ✅ Room Expiry

- For quick testing, modify expiry in code to 1 minute
- Watch for warning notifications
- Room automatically closes and deletes

## Common Issues

### Port Already in Use

If `http://localhost:5173` is busy:

```bash
dotnet run --urls "http://localhost:5000"
```

### SignalR Connection Fails

- Check browser console (F12) for errors
- Ensure app is running
- Try refreshing the page

### QR Code Not Showing

- Check that QRCoder package is installed
- Rebuild the project: `dotnet build`

### Build Errors

Clean and rebuild:

```bash
dotnet clean
dotnet build
```

## Development Tips

### Hot Reload

The app supports hot reload for code changes:

1. Keep `dotnet run` running
2. Edit .cs files
3. Changes apply automatically

### View Changes

For Razor view changes:

1. Just refresh the browser
2. No rebuild needed

### Check Logs

Watch the terminal for:

- Connection messages
- Room creation/deletion
- Participant joins/leaves
- Timer service updates

## Quick Command Reference

```bash
# Build project
dotnet build

# Run application
dotnet run

# Run on specific port
dotnet run --urls "http://localhost:5000"

# Clean build artifacts
dotnet clean

# Restore packages
dotnet restore

# Check for errors
dotnet build --no-incremental
```

## Features to Test

### Must Test ✨

- [x] Create room
- [x] Join room
- [x] Send messages
- [x] Real-time updates
- [x] Participant list
- [x] Timer countdown

### Should Test 🎯

- [x] Vote to close
- [x] Leave room
- [x] Share room (copy link)
- [x] QR code display
- [x] Typing indicators

### Nice to Test 💎

- [x] Multiple participants (3+)
- [x] Network disconnect/reconnect
- [x] Mobile responsive view (resize browser)
- [x] Different room types (1-on-1)
- [x] Different expiry times

## Next Steps

Once you've tested locally:

1. Review the code structure in README.md
2. Check out the roadmap for future enhancements
3. Consider deploying to Azure App Service
4. Add to your portfolio website

## Need Help?

Common questions:

- **How do I stop the app?** Press `Ctrl+C` in terminal
- **Where are rooms stored?** In-memory only (lost on restart)
- **Can I change colors?** Edit `ColorService.cs`
- **How to add more room types?** Extend `RoomType` enum

---

**Ready to test?** Just run `dotnet run` and go to http://localhost:5173! 🚀
