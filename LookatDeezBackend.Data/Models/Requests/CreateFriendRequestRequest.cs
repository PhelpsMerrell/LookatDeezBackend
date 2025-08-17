using System.ComponentModel.DataAnnotations;

namespace LookatDeezBackend.Data.Models.Requests
{
    public class CreateFriendRequestRequest
    {
        [Required]
        public string ToUserId { get; set; }
    }
}
