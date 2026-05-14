using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KoenZomers.Ring.Api.Entities
{
    /// <summary>
    /// Periodic footage clips created by Ring from historical snapshot capture.
    /// </summary>
    public class PeriodicFootageResponse
    {
        [JsonPropertyName("data")]
        public List<PeriodicFootage> Data { get; set; } = new();
    }

    public class PeriodicFootage
    {
        [JsonPropertyName("start_ms")]
        public long StartMilliseconds { get; set; }

        [JsonPropertyName("end_ms")]
        public long EndMilliseconds { get; set; }

        [JsonPropertyName("playback_ms")]
        public long PlaybackMilliseconds { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("deleted")]
        public bool Deleted { get; set; }

        [JsonPropertyName("snapshots")]
        public List<long> Snapshots { get; set; } = new();
    }
}
