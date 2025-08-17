namespace LookatDeezBackend.Data.Models.Responses
{
    public class FriendRequestResponse
    {
        public string Id { get; set; }
        public string FromUserId { get; set; }
        public string FromUserDisplayName { get; set; }
        public string ToUserId { get; set; }
        public string ToUserDisplayName { get; set; }
        public FriendRequestStatus Status { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
    }

    public class FriendRequestsEnvelope
    {
        public List<FriendRequestResponse> Sent { get; set; } = new List<FriendRequestResponse>();
        public List<FriendRequestResponse> Received { get; set; } = new List<FriendRequestResponse>();
    }

    public class FriendResponse
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public DateTime FriendsSince { get; set; }
    }
}
