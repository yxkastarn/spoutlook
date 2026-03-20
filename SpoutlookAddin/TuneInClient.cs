using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SpoutlookAddin
{
    /// <summary>
    /// Lightweight wrapper around the public Radio Browser API
    /// (<c>https://api.radio-browser.info</c>).
    ///
    /// No API key is required. The API is community-maintained and free to use.
    /// </summary>
    public sealed class TuneInClient : IDisposable
    {
        // Uses the German node; other nodes: nl1, at1, fr1, etc.
        private const string ApiBase = "https://de1.api.radio-browser.info/json";

        private readonly HttpClient _http;
        private bool _disposed;

        public TuneInClient()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add(
                "User-Agent", "SpoutlookAddin/1.0 (github.com/yxkastarn/spoutlook)");
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
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
        //  API calls
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns the top-voted radio stations ordered by vote count.
        /// </summary>
        /// <param name="limit">Maximum number of stations to return (default 30).</param>
        public async Task<IReadOnlyList<RadioStation>> GetTopStationsAsync(int limit = 30)
        {
            var url = $"{ApiBase}/stations/topvote?limit={limit}&hidebroken=true";
            return await FetchStationsAsync(url);
        }

        /// <summary>
        /// Searches for stations whose name contains <paramref name="query"/>.
        /// </summary>
        /// <param name="query">Partial station name to search for.</param>
        /// <param name="limit">Maximum number of results (default 30).</param>
        public async Task<IReadOnlyList<RadioStation>> SearchStationsAsync(
            string query, int limit = 30)
        {
            var encoded = Uri.EscapeDataString(query);
            var url = $"{ApiBase}/stations/search?name={encoded}&limit={limit}&hidebroken=true";
            return await FetchStationsAsync(url);
        }

        // ------------------------------------------------------------------ //
        //  Helpers
        // ------------------------------------------------------------------ //

        private async Task<IReadOnlyList<RadioStation>> FetchStationsAsync(string url)
        {
            var json = await _http.GetStringAsync(url);
            return JsonConvert.DeserializeObject<List<RadioStation>>(json)
                   ?? new List<RadioStation>();
        }
    }
}
