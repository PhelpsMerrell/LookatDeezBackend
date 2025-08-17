using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LookatDeezBackend.Data.Models
{
    public class PlaylistItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("addedAt")]
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("addedBy")]
        public string AddedBy { get; set; }

        [JsonPropertyName("order")]
        public int Order { get; set; } = 0;
    }
}
