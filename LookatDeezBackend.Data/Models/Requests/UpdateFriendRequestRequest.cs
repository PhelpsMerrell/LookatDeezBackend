using System.ComponentModel.DataAnnotations;

namespace LookatDeezBackend.Data.Models.Requests
{
    public class UpdateFriendRequestRequest
    {
        [Required]
        public FriendRequestStatus Status { get; set; }
    }
}
