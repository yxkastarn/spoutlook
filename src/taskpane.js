/**
 * Spoutlook – Task Pane Script
 *
 * Handles source selection (Spotify / TuneIn), Spotify OAuth + embed,
 * and TuneIn channel browsing + playback via Radio Browser API.
 */

/* ── Constants ─────────────────────────────────────────── */

/**
 * Replace SPOTIFY_CLIENT_ID with your own Spotify application client ID
 * from https://developer.spotify.com/dashboard
 */
const SPOTIFY_CLIENT_ID = "YOUR_SPOTIFY_CLIENT_ID";
const SPOTIFY_REDIRECT_URI = window.location.origin + "/callback.html";
const SPOTIFY_SCOPES = [
  "streaming",
  "user-read-email",
  "user-read-private",
  "user-read-playback-state",
  "user-modify-playback-state",
].join(" ");

/** Radio Browser API (free, open radio directory) */
const RADIO_API_BASE = "https://de1.api.radio-browser.info/json";

/* ── State ──────────────────────────────────────────────── */

let currentSource = "spotify"; // 'spotify' | 'tunein'
let spotifyAccessToken = null;
let tuneInSearchTimeout = null;
let spotifySearchTimeout = null;

/* ── Initialisation ─────────────────────────────────────── */

document.addEventListener("DOMContentLoaded", () => {
  restoreSource();
  restoreSpotifyToken();
  loadPopularStations();
  handleSpotifyCallback();
});

/* ── Source selector ────────────────────────────────────── */

/**
 * Switch between 'spotify' and 'tunein' sources.
 * @param {string} source
 */
function selectSource(source) {
  if (source === currentSource) return;
  currentSource = source;

  // Update buttons
  document.getElementById("btn-spotify").classList.toggle("active", source === "spotify");
  document.getElementById("btn-tunein").classList.toggle("active", source === "tunein");
  document.getElementById("btn-spotify").setAttribute("aria-pressed", String(source === "spotify"));
  document.getElementById("btn-tunein").setAttribute("aria-pressed", String(source === "tunein"));

  // Show / hide panels
  document.getElementById("panel-spotify").classList.toggle("active", source === "spotify");
  document.getElementById("panel-spotify").classList.toggle("hidden", source !== "spotify");
  document.getElementById("panel-tunein").classList.toggle("active", source === "tunein");
  document.getElementById("panel-tunein").classList.toggle("hidden", source !== "tunein");

  localStorage.setItem("spoutlook_source", source);
}

function restoreSource() {
  const saved = localStorage.getItem("spoutlook_source");
  if (saved && saved !== currentSource) {
    selectSource(saved);
  }
}

/* ── Spotify ────────────────────────────────────────────── */

function connectSpotify() {
  if (!SPOTIFY_CLIENT_ID || SPOTIFY_CLIENT_ID === "YOUR_SPOTIFY_CLIENT_ID") {
    showToast("Ange ett giltigt Spotify Client ID i taskpane.js.", true);
    return;
  }
  const state = generateRandomString(16);
  sessionStorage.setItem("spotify_auth_state", state);

  const params = new URLSearchParams({
    response_type: "token",
    client_id: SPOTIFY_CLIENT_ID,
    scope: SPOTIFY_SCOPES,
    redirect_uri: SPOTIFY_REDIRECT_URI,
    state,
  });

  window.open(
    "https://accounts.spotify.com/authorize?" + params.toString(),
    "spotify-auth",
    "width=480,height=640"
  );
}

/** Called by callback.html via window.opener.onSpotifyToken */
window.onSpotifyToken = function (token) {
  spotifyAccessToken = token;
  localStorage.setItem("spoutlook_spotify_token", token);
  showSpotifyPlayer();
  showToast("Spotify ansluten!");
};

function handleSpotifyCallback() {
  // Also handle implicit-grant redirect directly in this window if needed
  const hash = window.location.hash;
  if (hash.includes("access_token")) {
    const params = new URLSearchParams(hash.slice(1));
    const token = params.get("access_token");
    if (token) {
      spotifyAccessToken = token;
      localStorage.setItem("spoutlook_spotify_token", token);
      window.history.replaceState(null, "", window.location.pathname);
      showSpotifyPlayer();
    }
  }
}

function restoreSpotifyToken() {
  const token = localStorage.getItem("spoutlook_spotify_token");
  if (token) {
    spotifyAccessToken = token;
    showSpotifyPlayer();
  }
}

function showSpotifyPlayer() {
  document.getElementById("spotify-login").classList.add("hidden");
  document.getElementById("spotify-player").classList.remove("hidden");
  // Load default Spotify embed (trending playlist)
  loadSpotifyEmbed("playlist/37i9dQZEVXbMDoHDwVN2tF");
}

/**
 * Load a Spotify embed for a given URI path, e.g. "track/4cOdK2wGLETKBW3PvgPWqT"
 * @param {string} uri  e.g. "playlist/...", "track/...", "album/..."
 */
function loadSpotifyEmbed(uri) {
  const container = document.getElementById("spotify-embed-container");
  container.innerHTML = `
    <iframe
      title="Spotify Player"
      src="https://open.spotify.com/embed/${encodeURIComponent(uri)}?utm_source=generator&theme=0"
      width="100%"
      height="152"
      frameborder="0"
      allowfullscreen
      allow="autoplay; clipboard-write; encrypted-media; fullscreen; picture-in-picture"
      loading="lazy"
    ></iframe>`;
}

function disconnectSpotify() {
  spotifyAccessToken = null;
  localStorage.removeItem("spoutlook_spotify_token");
  document.getElementById("spotify-embed-container").innerHTML = "";
  document.getElementById("spotify-results").innerHTML = "";
  document.getElementById("spotify-search-input").value = "";
  document.getElementById("spotify-player").classList.add("hidden");
  document.getElementById("spotify-login").classList.remove("hidden");
  showToast("Utloggad från Spotify.");
}

/* ── Spotify search ─────────────────────────────────────── */

function debounceSpotifySearch(query) {
  clearTimeout(spotifySearchTimeout);
  const q = query.trim();
  if (!q) {
    document.getElementById("spotify-results").innerHTML = "";
    return;
  }
  spotifySearchTimeout = setTimeout(() => searchSpotify(q), 400);
}

async function searchSpotify(query) {
  if (!spotifyAccessToken) return;
  const list = document.getElementById("spotify-results");
  list.innerHTML = "<li class='loading'>Söker…</li>";
  try {
    const params = new URLSearchParams({ q: query, type: "track,album,playlist", limit: 10 });
    const res = await fetch("https://api.spotify.com/v1/search?" + params, {
      headers: { Authorization: "Bearer " + spotifyAccessToken },
    });

    if (res.status === 401) {
      disconnectSpotify();
      showToast("Spotify-sessionen har löpt ut. Logga in igen.", true);
      return;
    }

    if (!res.ok) throw new Error("Spotify API svarade " + res.status);
    const data = await res.json();
    renderSpotifyResults(data, list);
  } catch (err) {
    list.innerHTML = "<li class='empty-msg'>Kunde inte hämta sökresultat.</li>";
    console.error("Spotify search error:", err);
  }
}

function renderSpotifyResults(data, list) {
  list.innerHTML = "";
  const tracks = (data.tracks && data.tracks.items) || [];
  const albums = (data.albums && data.albums.items) || [];
  const playlists = (data.playlists && data.playlists.items) || [];
  const items = [...tracks.slice(0, 4), ...albums.slice(0, 3), ...playlists.slice(0, 3)];

  if (!items.length) {
    list.innerHTML = "<li class='empty-msg'>Inga resultat hittades.</li>";
    return;
  }

  items.forEach((item) => {
    if (!item) return;
    const li = document.createElement("li");
    li.setAttribute("role", "option");
    li.setAttribute("tabindex", "0");

    const img = item.images && item.images[0] ? item.images[0].url
      : item.album && item.album.images && item.album.images[0] ? item.album.images[0].url
      : null;

    const artistText = item.artists ? item.artists.map((a) => a.name).join(", ") : item.type;
    const typeLabel = { track: "Låt", album: "Album", playlist: "Spellista" }[item.type] || item.type;

    const uriPath = item.type + "/" + item.id;

    li.innerHTML = `
      ${img
        ? `<img class="item-logo" src="${sanitizeUrl(img)}" alt="" loading="lazy" />`
        : `<div class="item-logo-placeholder">🎵</div>`
      }
      <div class="item-info">
        <div class="item-name">${escapeHtml(item.name)}</div>
        <div class="item-sub">${escapeHtml(typeLabel)} · ${escapeHtml(artistText)}</div>
      </div>`;

    li.addEventListener("click", () => loadSpotifyEmbed(uriPath));
    li.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        loadSpotifyEmbed(uriPath);
      }
    });

    list.appendChild(li);
  });
}

/* ── TuneIn / Radio Browser ─────────────────────────────── */

async function loadPopularStations() {
  const list = document.getElementById("tunein-popular-list");
  try {
    const res = await fetch(
      // hidebroken=true excludes stations with broken stream URLs
      `${RADIO_API_BASE}/stations/topvote?limit=20&hidebroken=true`,
      { headers: { "User-Agent": "Spoutlook/1.0 (github.com/yxkastarn/spoutlook)" } }
    );
    if (!res.ok) throw new Error("Radio API svarade " + res.status);
    const stations = await res.json();
    renderStationList(stations, list);
  } catch (err) {
    list.innerHTML = "<li class='empty-msg'>Kunde inte ladda kanaler.</li>";
    console.error("Radio Browser error:", err);
  }
}

function debounceTuneInSearch(query) {
  clearTimeout(tuneInSearchTimeout);
  const q = query.trim();
  const searchSection = document.getElementById("tunein-search-results-section");
  const popularSection = document.getElementById("tunein-popular");
  if (!q) {
    searchSection.classList.add("hidden");
    popularSection.classList.remove("hidden");
    return;
  }
  tuneInSearchTimeout = setTimeout(() => searchTuneIn(q), 400);
}

async function searchTuneIn(query) {
  const searchSection = document.getElementById("tunein-search-results-section");
  const popularSection = document.getElementById("tunein-popular");
  const list = document.getElementById("tunein-results");

  searchSection.classList.remove("hidden");
  popularSection.classList.add("hidden");
  list.innerHTML = "<li class='loading'>Söker…</li>";

  try {
    const encoded = encodeURIComponent(query);
    const res = await fetch(
      `${RADIO_API_BASE}/stations/search?name=${encoded}&limit=20&hidebroken=true`,
      { headers: { "User-Agent": "Spoutlook/1.0 (github.com/yxkastarn/spoutlook)" } }
    );
    if (!res.ok) throw new Error("Radio API svarade " + res.status);
    const stations = await res.json();
    renderStationList(stations, list);
  } catch (err) {
    list.innerHTML = "<li class='empty-msg'>Sökning misslyckades.</li>";
    console.error("TuneIn search error:", err);
  }
}

function renderStationList(stations, list) {
  list.innerHTML = "";
  if (!stations || !stations.length) {
    list.innerHTML = "<li class='empty-msg'>Inga kanaler hittades.</li>";
    return;
  }

  stations.forEach((station) => {
    const li = document.createElement("li");
    li.setAttribute("role", "option");
    li.setAttribute("tabindex", "0");

    li.innerHTML = `
      ${station.favicon
        ? `<img class="item-logo" src="${sanitizeUrl(station.favicon)}" alt="" loading="lazy"
             onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" />
           <div class="item-logo-placeholder" style="display:none">📻</div>`
        : `<div class="item-logo-placeholder">📻</div>`
      }
      <div class="item-info">
        <div class="item-name">${escapeHtml(station.name)}</div>
        <div class="item-sub">${escapeHtml(station.country || "")}${station.tags ? " · " + escapeHtml(station.tags.split(",")[0]) : ""}</div>
      </div>`;

    li.addEventListener("click", () => playTuneInStation(station));
    li.addEventListener("keydown", (e) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        playTuneInStation(station);
      }
    });

    list.appendChild(li);
  });
}

function playTuneInStation(station) {
  const container = document.getElementById("tunein-player-container");
  const embed = document.getElementById("tunein-embed");
  const nameEl = document.getElementById("tunein-now-playing-name");

  nameEl.textContent = station.name;

  // Use the station's direct stream URL if it's an HTTP(S) stream
  // Radio Browser provides direct stream URLs; we load them in an iframe with an audio tag
  if (station.url_resolved && /^https?:\/\//i.test(station.url_resolved)) {
    embed.src = buildAudioPlayerUrl(station.url_resolved, station.name);
  } else {
    embed.src = buildAudioPlayerUrl(station.url, station.name);
  }

  container.classList.remove("hidden");

  // Register station click with Radio Browser (improves API rankings)
  reportStationClick(station.stationuuid);
}

/**
 * Build a small inline data: URI HTML page that plays the given stream URL.
 * This avoids mixed-content issues and keeps playback inside the task pane.
 * @param {string} streamUrl
 * @param {string} stationName
 * @returns {string} data URI
 */
function buildAudioPlayerUrl(streamUrl, stationName) {
  const safeUrl = sanitizeUrl(streamUrl);
  const safeName = escapeHtml(stationName);
  const html = `<!DOCTYPE html>
<html>
<head><meta charset="utf-8">
<style>
  body{margin:0;background:#1e1e1e;display:flex;align-items:center;justify-content:center;height:100vh;font-family:sans-serif}
  audio{width:100%;max-width:320px}
  p{color:#888;font-size:11px;text-align:center;margin-top:6px}
</style>
</head>
<body>
<div>
  <audio controls autoplay src="${safeUrl}">
    Din webbläsare stöder inte ljuduppspelning.
  </audio>
  <p>${safeName}</p>
</div>
</body>
</html>`;
  return "data:text/html;charset=utf-8," + encodeURIComponent(html);
}

async function reportStationClick(uuid) {
  if (!uuid) return;
  try {
    await fetch(`${RADIO_API_BASE}/url/${encodeURIComponent(uuid)}`, {
      method: "GET",
      headers: { "User-Agent": "Spoutlook/1.0 (github.com/yxkastarn/spoutlook)" },
    });
  } catch (_) {
    // Non-critical; ignore errors
  }
}

function stopTuneIn() {
  const container = document.getElementById("tunein-player-container");
  const embed = document.getElementById("tunein-embed");
  embed.src = "";
  container.classList.add("hidden");
}

/* ── Utilities ──────────────────────────────────────────── */

/**
 * Escape HTML special characters to prevent XSS when inserting into innerHTML.
 * @param {string} str
 * @returns {string}
 */
function escapeHtml(str) {
  if (!str) return "";
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

/**
 * Validate that a URL uses an allowed protocol (http/https/data).
 * Returns the URL if safe, otherwise returns an empty string.
 * @param {string} url
 * @returns {string}
 */
function sanitizeUrl(url) {
  if (!url) return "";
  try {
    const parsed = new URL(url);
    if (["http:", "https:", "data:"].includes(parsed.protocol)) {
      return url;
    }
    return "";
  } catch (_) {
    return "";
  }
}

/**
 * Generate a cryptographically random string of the given length.
 * @param {number} length
 * @returns {string}
 */
function generateRandomString(length) {
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  const array = new Uint8Array(length);
  window.crypto.getRandomValues(array);
  return Array.from(array)
    .map((b) => chars[b % chars.length])
    .join("");
}

/**
 * Show a toast notification.
 * @param {string} message
 * @param {boolean} [isError=false]
 */
function showToast(message, isError = false) {
  const toast = document.getElementById("status-toast");
  toast.textContent = message;
  toast.classList.toggle("error", isError);
  toast.classList.remove("hidden");
  clearTimeout(showToast._timer);
  showToast._timer = setTimeout(() => toast.classList.add("hidden"), 3500);
}
