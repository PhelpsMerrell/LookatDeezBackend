using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LookatDeezBackend.Data.Models
{
    public class FriendRequest
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("fromUserId")]
        [JsonProperty("fromUserId")]
        public string FromUserId { get; set; }

        [JsonPropertyName("toUserId")]
        [JsonProperty("toUserId")]
        public string ToUserId { get; set; }

        [JsonPropertyName("status")]
        [JsonProperty("status")]
        public string Status { get; set; } = "pending"; // "pending", "accepted", "declined"

        [JsonPropertyName("requestedAt")]
        [JsonProperty("requestedAt")]
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("respondedAt")]
        [JsonProperty("respondedAt")]
        public DateTime? RespondedAt { get; set; }
    }
}
