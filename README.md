# spoutlook

A **Spotify & TuneIn mini-player plugin for Classic Outlook** (VSTO Add-in).

Log in with your Spotify account or browse TuneIn radio channels and control playback directly from an Outlook task pane — no need to switch windows.

---

## Features

### Spotify
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

### TuneIn / Radio
| Feature | Details |
|---|---|
| 📻 Browse stations | Popular stations loaded automatically from Radio Browser API |
| 🔍 Search | Real-time station search by name |
| ▶ Stream | One-click playback via `MediaElement` |
| ⏹ Stop | Stop the current stream at any time |

---

## Architecture

```
SpoutlookAddin/
├── SpoutlookAddin.csproj       – SDK-style project (net472, WPF + WinForms)
├── ThisAddIn.cs                – VSTO entry point; registers the Custom Task Pane
├── Ribbon.xml                  – Ribbon XML (embedded resource)
├── Ribbon.cs                   – IRibbonExtensibility; "Mini Player" toggle button
├── MiniPlayerHostControl.cs    – WinForms ElementHost wrapper (required by VSTO CTP)
├── MiniPlayerControl.xaml      – WPF UI: source selector + Spotify + TuneIn panels
├── MiniPlayerControl.xaml.cs   – UI code-behind (polling, button handlers, TuneIn)
├── SpotifyAuth.cs              – OAuth 2.0 PKCE login + token storage
├── SpotifyApiClient.cs         – Spotify Web API calls (play, pause, next, …)
├── SpotifyModels.cs            – JSON response models
├── TuneInClient.cs             – Radio Browser API wrapper (search + top stations)
├── TuneInModels.cs             – Radio Browser JSON models
├── app.config
└── Properties/AssemblyInfo.cs

src/                            – Optional Office Add-in (web) for Outlook on the web
├── taskpane.html / .css / .js
├── callback.html
└── commands.html
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

> \* Playback control via the Spotify Web API requires a **Spotify Premium** account.  
> TuneIn radio playback works with any account via the free [Radio Browser API](https://api.radio-browser.info/).

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

### 3. Build & install

1. Open `SpoutlookAddin.csproj` in Visual Studio.
2. Select **Debug** → **Start Debugging** (`F5`) to build and sideload the add-in.
3. Outlook opens with the **Spoutlook** group in the Mail ribbon.

---

## Usage

1. Open an email in Outlook.
2. Click **Mini Player** in the ribbon to open the task pane.
3. Use the **Spotify / TuneIn** toggle at the top to choose your source.

### Spotify
- Click **Log in to Spotify** and complete the OAuth flow in the browser window.
- The player shows the currently playing track with full controls.

### TuneIn
- Popular stations load automatically.
- Type in the search box to find stations by name.
- Click a station to start streaming.
- Click **Stop** to end playback.

---

## License

MIT
