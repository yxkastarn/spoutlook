# spoutlook

A **Spotify mini-player plugin for Classic Outlook** (VSTO Add-in).

Log in with your Spotify account and control playback directly from an Outlook task pane — no need to switch windows.

---

## Features

| Feature | Details |
|---|---|
| 🎵 Now-playing | Shows track name, artist, and album art |
| ▶ / ⏸ Play & Pause | One-click playback toggle |
| ⏮ / ⏭ Previous / Next | Skip tracks |
| 🔊 Volume control | Slider synced with the active Spotify device |
| ⏩ Seek | Drag the progress bar to jump to any position |
| 🔐 Secure login | OAuth 2.0 + PKCE – no password stored |
| 💾 Persistent login | Tokens saved in isolated storage; stay logged in across Outlook restarts |
| 🔄 Auto-refresh | Access token refreshed transparently before it expires |

---

## Architecture

```
SpoutlookAddin/
├── SpoutlookAddin.csproj     – SDK-style project (net472, WPF + WinForms)
├── ThisAddIn.cs              – VSTO entry point; registers the Custom Task Pane
├── Ribbon.xml                – Ribbon XML (embedded resource)
├── Ribbon.cs                 – IRibbonExtensibility; "Mini Player" toggle button
├── MiniPlayerHostControl.cs  – WinForms ElementHost wrapper (required by VSTO CTP)
├── MiniPlayerControl.xaml    – WPF UI (album art, controls, sliders)
├── MiniPlayerControl.xaml.cs – UI code-behind (polling, button handlers)
├── SpotifyAuth.cs            – OAuth 2.0 PKCE login + token storage
├── SpotifyApiClient.cs       – Spotify Web API calls (play, pause, next, …)
├── SpotifyModels.cs          – JSON response models
├── app.config
└── Properties/AssemblyInfo.cs
```

---

## Requirements

| Requirement | Version |
|---|---|
| Windows | 10 or 11 |
| .NET Framework | 4.7.2 or later |
| Visual Studio | 2019 / 2022 (with **Office/SharePoint development** workload) |
| Outlook | Classic Outlook 2016, 2019, 2021, or Microsoft 365 |
| Spotify account | Free or Premium (\*) |

> \* Playback control via the Web API requires a **Spotify Premium** account.

---

## Setup

### 1. Register a Spotify app

1. Go to the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard).
2. Click **Create app**.
3. Set **Redirect URI** to: `http://localhost:5678/callback`
4. Note the **Client ID**.

### 2. Add your Client ID

Open `SpoutlookAddin/app.config` and replace the placeholder value:

```xml
<add key="SpotifyClientId" value="paste_your_client_id_here" />
```

> **Do not** commit `app.config` with a real Client ID to public version control.
> You can add it to `.gitignore` or use a user-specific override file.

### 3. Build & deploy

```powershell
# Open Developer PowerShell for VS (or use Visual Studio)
cd SpoutlookAddin
dotnet restore
dotnet build -c Release
```

Then in Visual Studio, right-click the project → **Publish** (ClickOnce) or use the **Build → Publish** wizard to generate an installer.

Alternatively, install directly for development:

1. Open `SpoutlookAddin.sln` in Visual Studio.
2. Press **F5** – Visual Studio will register the add-in and launch Outlook in debug mode.

### 4. Using the add-in

1. Open **Classic Outlook**.
2. In the **Home** / **Mail** tab, click **Mini Player** in the **Spotify** group.
3. The side pane opens. Click **Log in with Spotify**.
4. Your browser opens the Spotify authorization page – grant access.
5. The pane switches to the player view and starts showing what's playing.

---

## Security notes

* Authentication uses **OAuth 2.0 Authorization Code with PKCE** – the app never sees your Spotify password.
* Tokens are stored in .NET **Isolated Storage** (per-user, per-assembly), not in plain text on disk or in the registry.
* The local callback server (`http://localhost:5678`) runs only for the duration of the login flow and is shut down immediately after receiving the code.

---

## Contributing

Pull requests are welcome. For major changes, open an issue first to discuss what you would like to change.

---

## License

MIT

