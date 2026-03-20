# Spoutlook

Spoutlook is a **Classic Outlook add-in** (Office Add-in) that adds a music miniplayer to your Outlook toolbar. You can choose to listen to either **Spotify** or **TuneIn** radio channels directly inside Outlook, without leaving your inbox.

---

## Features

- 🎵 **Source selector** – Switch between Spotify and TuneIn with one click
- **Spotify** – Log in with your Spotify account, search for tracks/albums/playlists, and play them via the embedded Spotify player (requires Spotify Premium for streaming)
- 📻 **TuneIn** – Browse the top-voted radio stations or search by name; playback is handled in-pane via a streaming audio player (powered by [Radio Browser API](https://api.radio-browser.info/))

---

## Project structure

```
spoutlook/
├── manifest.xml          # Office Add-in manifest (sideload this into Outlook)
├── package.json
├── src/
│   ├── taskpane.html     # Main task pane UI
│   ├── taskpane.css      # Styles
│   ├── taskpane.js       # All application logic
│   ├── callback.html     # Spotify OAuth implicit-grant callback page
│   ├── commands.html     # Ribbon command stub (required by manifest)
│   └── assets/           # Add-in icons (SVG)
└── README.md
```

---

## Setup

### 1. Create a Spotify application

1. Go to [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and create a new app.
2. Set the **Redirect URI** to `https://localhost:3000/callback.html`.
3. Copy your **Client ID** and open `src/taskpane.js`.
4. Replace `YOUR_SPOTIFY_CLIENT_ID` on line 12 with your Client ID:

```js
const SPOTIFY_CLIENT_ID = "your_actual_client_id_here";
```

### 2. Serve the add-in locally

```bash
npm install
npm start
```

This starts a local HTTP server on `http://localhost:3000` serving the `src/` directory.

> **Note:** For production use, serve the files over HTTPS. Update the URLs in `manifest.xml` accordingly.

### 3. Sideload the add-in into Outlook

**Classic Outlook (Windows):**

1. In Outlook, go to **File → Manage Add-ins** (or **Get Add-ins**).
2. Choose **Add a custom add-in → From file…**
3. Select `manifest.xml` from this repository.
4. The **Spoutlook** button will appear in the ribbon when you open an email.

**Outlook on the web:**

1. Go to <https://outlook.office.com> → Settings → **Manage add-ins**.
2. Click **+** → **Add from file** and upload `manifest.xml`.

---

## Usage

1. Open an email in Outlook.
2. Click **Open Spoutlook** in the ribbon.
3. In the task pane, click **Spotify** or **TuneIn** at the top to choose your source.

### Spotify
- Click **Anslut Spotify** to log in (a pop-up opens).
- Use the search box to find tracks, albums, or playlists.
- Click a result to load it into the embedded player.

### TuneIn
- The pane loads the most popular radio stations automatically.
- Type in the search box to find stations by name.
- Click a station to start playing.
- Click **■** (stop) to stop playback.

---

## Development

```bash
npm install       # Install dev dependencies
npm start         # Serve src/ on http://localhost:3000
npm run lint      # Lint JavaScript files
```

---

## Tech stack

| Area | Technology |
|------|-----------|
| Add-in framework | [Office Add-ins (web)](https://learn.microsoft.com/office/dev/add-ins/) |
| Spotify | [Spotify Web API](https://developer.spotify.com/documentation/web-api) · Spotify Embed |
| Radio | [Radio Browser API](https://api.radio-browser.info/) (free, open) |
| UI | Vanilla HTML / CSS / JavaScript (no build step) |

---

## License

MIT
