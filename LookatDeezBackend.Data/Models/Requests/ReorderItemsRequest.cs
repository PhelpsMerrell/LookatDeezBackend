using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LookatDeezBackend.Data.Models.Requests
{
    public class ReorderItemsRequest
    {
        [JsonPropertyName("itemOrder")]
        [Required]
        public List<string> ItemOrder { get; set; } = new List<string>();
    }
}
