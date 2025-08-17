using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace LookatDeezBackend.Data.Models
{
    public class Playlist

    {
        
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("title")]
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonPropertyName("ownerId")]
        [JsonProperty("ownerId")]
        public string OwnerId { get; set; }

        [JsonPropertyName("createdAt")]
        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("updatedAt")]
        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("isPublic")]
        [JsonProperty("isPublic")]
        public bool IsPublic { get; set; } = false;

        [JsonPropertyName("items")]
        [JsonProperty("items")] 
        public List<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
    }

   

}
