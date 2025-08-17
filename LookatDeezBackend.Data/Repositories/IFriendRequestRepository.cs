using LookatDeezBackend.Data.Models;

namespace LookatDeezBackend.Data.Repositories
{
    public interface IFriendRequestRepository
    {
        Task<FriendRequest> CreateFriendRequestAsync(FriendRequest friendRequest);
        Task<FriendRequest> GetFriendRequestByIdAsync(string requestId);
        Task<FriendRequest> UpdateFriendRequestAsync(FriendRequest friendRequest);
        Task<bool> DeleteFriendRequestAsync(string requestId, string fromUserId);
        Task<List<FriendRequest>> GetSentRequestsAsync(string fromUserId);
        Task<List<FriendRequest>> GetReceivedRequestsAsync(string toUserId);
        Task<FriendRequest> GetExistingRequestAsync(string fromUserId, string toUserId);
        Task<List<FriendRequest>> GetPendingRequestsAsync(string userId);
    }
}
