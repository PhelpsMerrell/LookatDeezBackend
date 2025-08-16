using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace LookatDeezBackend.Data.Models
{
  
    public class Permission
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("playlistId")]
        public string PlaylistId { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("permission")]
        public string PermissionLevel { get; set; } // "view", "edit", "admin"

        [JsonPropertyName("grantedBy")]
        public string GrantedBy { get; set; }

        [JsonPropertyName("grantedAt")]
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    }
    
  
}
