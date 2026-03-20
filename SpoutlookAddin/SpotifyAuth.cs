using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.IsolatedStorage;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;

namespace SpoutlookAddin
{
    /// <summary>
    /// Implements the Spotify OAuth 2.0 Authorization Code flow with PKCE.
    ///
    /// Flow:
    ///   1. <see cref="StartLoginAsync"/> generates a PKCE challenge, opens the
    ///      user's browser to the Spotify authorization page, and starts a local
    ///      HTTP listener on <c>http://localhost:5678/callback</c>.
    ///   2. After the user grants permission, Spotify redirects to the local
    ///      listener with an authorization code.
    ///   3. <see cref="ExchangeCodeAsync"/> exchanges the code for access and
    ///      refresh tokens, which are persisted to isolated storage.
    ///
    /// The Client ID is read from <c>app.config</c> (appSettings key
    /// <c>SpotifyClientId</c>) so that it is never hardcoded in source.
    /// </summary>
    public sealed class SpotifyAuth : IDisposable
    {
        // ------------------------------------------------------------------ //
        //  Constants
        // ------------------------------------------------------------------ //

        private static readonly string ClientId =
            ConfigurationManager.AppSettings["SpotifyClientId"]
            ?? throw new InvalidOperationException(
                "SpotifyClientId is not set in app.config. " +
                "Add <add key=\"SpotifyClientId\" value=\"...\"/> to <appSettings>.");

        private const string RedirectUri   = "http://localhost:5678/callback/";
        private const string AuthEndpoint  = "https://accounts.spotify.com/authorize";
        private const string TokenEndpoint = "https://accounts.spotify.com/api/token";
        private const string StorageFile   = "spoutlook_tokens.json";
        /// <summary>Expected Host header on the OAuth callback to guard against DNS rebinding.</summary>
        private const string ExpectedHost  = "localhost:5678";

        private static readonly string[] Scopes =
        {
            "user-read-playback-state",
            "user-modify-playback-state",
            "user-read-currently-playing",
        };

        // ------------------------------------------------------------------ //
        //  State
        // ------------------------------------------------------------------ //

        private string? _accessToken;
        private string? _refreshToken;
        private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

        // PKCE verifier kept in memory for the duration of the login exchange.
        private string? _codeVerifier;

        private readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient();
        private bool _disposed;

        // ------------------------------------------------------------------ //
        //  Public properties
        // ------------------------------------------------------------------ //

        public bool IsAuthenticated =>
            !string.IsNullOrEmpty(_accessToken) &&
            !string.IsNullOrEmpty(_refreshToken);

        public string? AccessToken => _accessToken;

        /// <summary>Fired when tokens are updated (login or refresh).</summary>
        public event EventHandler? TokensUpdated;

        // ------------------------------------------------------------------ //
        //  Login
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Opens the browser to the Spotify login page and waits (asynchronously)
        /// for the OAuth callback. Returns <c>true</c> if login succeeded.
        /// </summary>
        public async Task<bool> StartLoginAsync()
        {
            _codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(_codeVerifier);
            var state = GenerateRandomString(16);

            var queryString = BuildQueryString(new Dictionary<string, string>
            {
                ["client_id"]             = ClientId,
                ["response_type"]         = "code",
                ["redirect_uri"]          = RedirectUri,
                ["code_challenge_method"] = "S256",
                ["code_challenge"]        = codeChallenge,
                ["state"]                 = state,
                ["scope"]                 = string.Join(" ", Scopes),
            });

            Process.Start(new ProcessStartInfo(AuthEndpoint + "?" + queryString)
            {
                UseShellExecute = true,
            });

            var code = await ListenForCallbackAsync(state);
            if (code == null) return false;

            return await ExchangeCodeAsync(code);
        }

        // ------------------------------------------------------------------ //
        //  Token management
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns a valid access token, refreshing it if it has expired.
        /// Returns <c>null</c> if the user is not authenticated.
        /// </summary>
        public async Task<string?> GetValidAccessTokenAsync()
        {
            if (!IsAuthenticated) return null;

            if (DateTimeOffset.UtcNow >= _tokenExpiry.AddMinutes(-1))
                await RefreshTokenAsync();

            return _accessToken;
        }

        // ------------------------------------------------------------------ //
        //  Persistent storage
        // ------------------------------------------------------------------ //

        /// <summary>Loads previously saved tokens from isolated storage.</summary>
        public void LoadTokensFromStorage()
        {
            try
            {
                using var store = IsolatedStorageFile.GetUserStoreForAssembly();
                if (!store.FileExists(StorageFile)) return;

                using var reader = new StreamReader(
                    new IsolatedStorageFileStream(StorageFile, FileMode.Open, store));
                var json = reader.ReadToEnd();
                var data = JsonConvert.DeserializeObject<TokenData>(json);
                if (data == null) return;

                _accessToken  = data.AccessToken;
                _refreshToken = data.RefreshToken;
                _tokenExpiry  = data.Expiry;
            }
            catch
            {
                // Ignore storage errors – user will need to log in again.
            }
        }

        /// <summary>Clears stored tokens (logout).</summary>
        public void Logout()
        {
            _accessToken  = null;
            _refreshToken = null;
            _tokenExpiry  = DateTimeOffset.MinValue;
            TryDeleteStorageFile();
            TokensUpdated?.Invoke(this, EventArgs.Empty);
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
        //  Private helpers
        // ------------------------------------------------------------------ //

        private async Task<bool> ExchangeCodeAsync(string code)
        {
            var body = BuildQueryString(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["redirect_uri"]  = RedirectUri,
                ["client_id"]     = ClientId,
                ["code_verifier"] = _codeVerifier!,
            });

            return await PostTokenRequestAsync(body);
        }

        private async Task<bool> RefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(_refreshToken)) return false;

            var body = BuildQueryString(new Dictionary<string, string>
            {
                ["grant_type"]    = "refresh_token",
                ["refresh_token"] = _refreshToken,
                ["client_id"]     = ClientId,
            });

            return await PostTokenRequestAsync(body);
        }

        private async Task<bool> PostTokenRequestAsync(string formBody)
        {
            var content = new System.Net.Http.StringContent(
                formBody,
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            var response = await _http.PostAsync(TokenEndpoint, content);
            if (!response.IsSuccessStatusCode) return false;

            var json  = await response.Content.ReadAsStringAsync();
            var token = JsonConvert.DeserializeObject<TokenResponse>(json);
            if (token == null) return false;

            _accessToken = token.AccessToken;
            if (!string.IsNullOrEmpty(token.RefreshToken))
                _refreshToken = token.RefreshToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

            SaveTokensToStorage();
            TokensUpdated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Starts a temporary localhost HTTP listener to receive the OAuth callback.
        /// The Host header is validated to <c>localhost:5678</c> to prevent DNS-rebinding
        /// attacks; requests from unexpected hosts are rejected with 400.
        /// </summary>
        private static async Task<string?> ListenForCallbackAsync(string expectedState)
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5678/callback/");
            listener.Start();

            // Wait at most 5 minutes for the user to complete login.
            var contextTask = listener.GetContextAsync();
            if (await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5))) != contextTask)
                return null;

            var context = await contextTask;

            // DNS-rebinding protection: reject requests whose Host header differs
            // from the expected loopback address/port.
            var host = context.Request.Headers["Host"] ?? "";
            if (!string.Equals(host, ExpectedHost, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                listener.Stop();
                return null;
            }

            var query = context.Request.QueryString;

            // Send a simple response to the browser.
            const string html =
                "<html><body style='font-family:sans-serif'>" +
                "<h2>✅ Spoutlook – Login successful!</h2>" +
                "<p>You can close this tab and return to Outlook.</p>" +
                "</body></html>";

            context.Response.ContentType = "text/html";
            var bytes = Encoding.UTF8.GetBytes(html);
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            context.Response.Close();
            listener.Stop();

            if (query["state"] != expectedState) return null;
            if (!string.IsNullOrEmpty(query["error"]))  return null;

            return query["code"];
        }

        private void SaveTokensToStorage()
        {
            try
            {
                var data = new TokenData
                {
                    AccessToken  = _accessToken,
                    RefreshToken = _refreshToken,
                    Expiry       = _tokenExpiry,
                };
                var json = JsonConvert.SerializeObject(data);

                using var store = IsolatedStorageFile.GetUserStoreForAssembly();
                using var writer = new StreamWriter(
                    new IsolatedStorageFileStream(StorageFile, FileMode.Create, store));
                writer.Write(json);
            }
            catch { /* Best-effort */ }
        }

        private static void TryDeleteStorageFile()
        {
            try
            {
                using var store = IsolatedStorageFile.GetUserStoreForAssembly();
                if (store.FileExists(StorageFile))
                    store.DeleteFile(StorageFile);
            }
            catch { }
        }

        // ------------------------------------------------------------------ //
        //  PKCE helpers
        // ------------------------------------------------------------------ //

        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using var rng = new RNGCryptoServiceProvider();
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string GenerateCodeChallenge(string verifier)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
            return Base64UrlEncode(hash);
        }

        private static string GenerateRandomString(int length)
        {
            var bytes = new byte[length];
            using var rng = new RNGCryptoServiceProvider();
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes).Substring(0, length);
        }

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static string BuildQueryString(Dictionary<string, string> parameters)
        {
            var parts = new List<string>();
            foreach (var kv in parameters)
                parts.Add($"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value)}");
            return string.Join("&", parts);
        }

        // ------------------------------------------------------------------ //
        //  Private DTOs
        // ------------------------------------------------------------------ //

        private sealed class TokenResponse
        {
            [JsonProperty("access_token")]  public string AccessToken  { get; set; } = "";
            [JsonProperty("refresh_token")] public string? RefreshToken { get; set; }
            [JsonProperty("expires_in")]    public int     ExpiresIn    { get; set; }
        }

        private sealed class TokenData
        {
            public string? AccessToken  { get; set; }
            public string? RefreshToken { get; set; }
            public DateTimeOffset Expiry { get; set; }
        }
    }
}
