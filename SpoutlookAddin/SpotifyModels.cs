using Newtonsoft.Json;

namespace SpoutlookAddin
{
    // ======================================================================= //
    //  Spotify Web API – response models (subset used by the mini-player)
    // ======================================================================= //

    /// <summary>Response from <c>GET /me/player/currently-playing</c>.</summary>
    public sealed class CurrentlyPlaying
    {
        [JsonProperty("is_playing")]
        public bool IsPlaying { get; set; }

        [JsonProperty("progress_ms")]
        public int ProgressMs { get; set; }

        [JsonProperty("item")]
        public Track? Item { get; set; }

        [JsonProperty("device")]
        public Device? Device { get; set; }
    }

    public sealed class Track
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("duration_ms")]
        public int DurationMs { get; set; }

        [JsonProperty("artists")]
        public Artist[] Artists { get; set; } = System.Array.Empty<Artist>();

        [JsonProperty("album")]
        public Album? Album { get; set; }
    }

    public sealed class Artist
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";
    }

    public sealed class Album
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("images")]
        public AlbumImage[] Images { get; set; } = System.Array.Empty<AlbumImage>();
    }

    public sealed class AlbumImage
    {
        [JsonProperty("url")]
        public string Url { get; set; } = "";

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }
    }

    public sealed class Device
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("volume_percent")]
        public int? VolumePercent { get; set; }
    }
}
