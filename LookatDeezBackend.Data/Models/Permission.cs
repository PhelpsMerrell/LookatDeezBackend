using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace LookatDeezBackend.Data.Models
{
  
    public class Permission
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("playlistId")]
        [JsonProperty("playlistId")]
        public string PlaylistId { get; set; }

        [JsonPropertyName("userId")]
        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("permission")]
        [JsonProperty("permission")]
        public string PermissionLevel { get; set; } // "view", "edit", "admin"

        [JsonPropertyName("grantedBy")]
        [JsonProperty("grantedBy")]
        public string GrantedBy { get; set; }

        [JsonPropertyName("grantedAt")]
        [JsonProperty("grantedAt")]
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    }
    
  
}
