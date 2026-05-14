using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KoenZomers.Ring.Api.Entities
{
    /// <summary>
    /// Historical Ring video events returned by the mobile-style video search endpoint.
    /// </summary>
    public class VideoSearchResponse
    {
        [JsonPropertyName("video_search")]
        public List<VideoSearchResult> VideoSearch { get; set; } = new();
    }

    public class VideoSearchResult
    {
        [JsonPropertyName("ding_id")]
        public string DingId { get; set; }

        [JsonPropertyName("created_at")]
        public long CreatedAt { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("duration")]
        public long Duration { get; set; }

        [JsonPropertyName("had_subscription")]
        public bool HadSubscription { get; set; }
    }
}
