using Newtonsoft.Json;

namespace SpoutlookAddin
{
    /// <summary>
    /// Represents a radio station returned by the Radio Browser API.
    /// </summary>
    public sealed class RadioStation
    {
        [JsonProperty("stationuuid")]
        public string StationUuid { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("url_resolved")]
        public string UrlResolved { get; set; } = "";

        [JsonProperty("url")]
        public string Url { get; set; } = "";

        [JsonProperty("country")]
        public string Country { get; set; } = "";

        [JsonProperty("tags")]
        public string Tags { get; set; } = "";

        [JsonProperty("favicon")]
        public string Favicon { get; set; } = "";

        /// <summary>
        /// Returns the best available stream URL (prefers the resolved URL).
        /// </summary>
        public string StreamUrl =>
            !string.IsNullOrWhiteSpace(UrlResolved) ? UrlResolved : Url;

        /// <summary>
        /// A short human-readable sub-text with country and first tag.
        /// </summary>
        public string SubText
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(Country)) parts.Add(Country);
                var firstTag = Tags?.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(firstTag)) parts.Add(firstTag);
                return string.Join(" · ", parts);
            }
        }

        public override string ToString() => Name;
    }
}
