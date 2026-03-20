using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SpoutlookAddin
{
    /// <summary>
    /// Thin wrapper around the Spotify Web API endpoints used by the mini-player.
    /// All methods automatically refresh the access token when necessary.
    ///
    /// Errors are surfaced as <see cref="SpotifyApiException"/> so the UI can
    /// display meaningful feedback to the user.
    /// </summary>
    public sealed class SpotifyApiClient : IDisposable
    {
        private const string BaseUrl = "https://api.spotify.com/v1";

        private readonly SpotifyAuth _auth;
        private readonly HttpClient  _http;
        private bool _disposed;

        public SpotifyApiClient(SpotifyAuth auth)
        {
            _auth = auth;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ------------------------------------------------------------------ //
        //  IDisposable
        // ------------------------------------------------------------------ //

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }

        // ------------------------------------------------------------------ //
        //  Playback read
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns the currently playing track, or <c>null</c> if nothing is
        /// playing or the user has no active device.
        /// </summary>
        public async Task<CurrentlyPlaying?> GetCurrentlyPlayingAsync()
        {
            var response = await GetAsync("/me/player/currently-playing");
            if (response == null || response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            await EnsureSuccessAsync(response);
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<CurrentlyPlaying>(json);
        }

        // ------------------------------------------------------------------ //
        //  Playback control
        // ------------------------------------------------------------------ //

        /// <summary>Resumes playback on the active device.</summary>
        public Task PlayAsync()   => PutAsync("/me/player/play", null);

        /// <summary>Pauses playback on the active device.</summary>
        public Task PauseAsync()  => PutAsync("/me/player/pause", null);

        /// <summary>Skips to the next track.</summary>
        public Task NextAsync()   => PostAsync("/me/player/next", null);

        /// <summary>Skips to the previous track.</summary>
        public Task PreviousAsync() => PostAsync("/me/player/previous", null);

        /// <summary>Sets the playback volume (0–100).</summary>
        public Task SetVolumeAsync(int percent) =>
            PutAsync($"/me/player/volume?volume_percent={Math.Clamp(percent, 0, 100)}", null);

        /// <summary>Seeks to the given position in the current track (milliseconds).</summary>
        public Task SeekAsync(int positionMs) =>
            PutAsync($"/me/player/seek?position_ms={positionMs}", null);

        // ------------------------------------------------------------------ //
        //  Private HTTP helpers
        // ------------------------------------------------------------------ //

        private async Task<HttpResponseMessage?> GetAsync(string path)
        {
            var token = await _auth.GetValidAccessTokenAsync();
            if (token == null) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _http.SendAsync(request);
        }

        private async Task PutAsync(string path, string? jsonBody)
        {
            var token = await _auth.GetValidAccessTokenAsync();
            if (token == null) return;

            using var request = new HttpRequestMessage(HttpMethod.Put, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (jsonBody != null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            await EnsureSuccessAsync(response);
        }

        private async Task PostAsync(string path, string? jsonBody)
        {
            var token = await _auth.GetValidAccessTokenAsync();
            if (token == null) return;

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (jsonBody != null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            await EnsureSuccessAsync(response);
        }

        /// <summary>
        /// Throws <see cref="SpotifyApiException"/> if the response indicates failure,
        /// including Spotify-specific error messages when available.
        /// </summary>
        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            string detail;
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                var err  = JsonConvert.DeserializeObject<SpotifyErrorEnvelope>(body);
                detail = err?.Error?.Message ?? body;
            }
            catch
            {
                detail = response.ReasonPhrase ?? response.StatusCode.ToString();
            }

            throw new SpotifyApiException((int)response.StatusCode, detail);
        }

        // ------------------------------------------------------------------ //
        //  Private error DTO
        // ------------------------------------------------------------------ //

        private sealed class SpotifyErrorEnvelope
        {
            [JsonProperty("error")] public SpotifyErrorBody? Error { get; set; }
        }

        private sealed class SpotifyErrorBody
        {
            [JsonProperty("status")]  public int    Status  { get; set; }
            [JsonProperty("message")] public string Message { get; set; } = "";
        }
    }

    /// <summary>
    /// Represents an error returned by the Spotify Web API.
    /// </summary>
    public sealed class SpotifyApiException : Exception
    {
        public int StatusCode { get; }

        public SpotifyApiException(int statusCode, string message)
            : base($"Spotify API error {statusCode}: {message}")
        {
            StatusCode = statusCode;
        }
    }
}
