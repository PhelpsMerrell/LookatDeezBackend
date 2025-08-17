using LookatDeezBackend.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using User = LookatDeezBackend.Data.Models.User;

namespace LookatDeezBackend.Data.Services
{
    public interface ICosmosService
    {
        Task<Playlist> CreatePlaylistAsync(Playlist playlist);
        Task<Playlist> GetPlaylistByIdAsync(string id);
        Task<IEnumerable<Playlist>> GetUserPlaylistsAsync(string userId);
        Task<IEnumerable<Playlist>> GetSharedPlaylistsAsync(string userId);
        Task UpdatePlaylistAsync(Playlist playlist);
        Task DeletePlaylistAsync(string id, string ownerId);
        Task<Permission> CreatePermissionAsync(Permission permission);
        Task<IEnumerable<Permission>> GetPlaylistPermissionsAsync(string playlistId);
        Task DeletePermissionAsync(string playlistId, string userId);
        Task<Permission> GetUserPermissionAsync(string playlistId, string userId);
        
        // Friend-related methods
        Task<FriendRequest> CreateFriendRequestAsync(FriendRequest friendRequest);
        Task<FriendRequest> GetFriendRequestByIdAsync(string requestId);
        Task<FriendRequest> UpdateFriendRequestAsync(FriendRequest friendRequest);
        Task<bool> DeleteFriendRequestAsync(string requestId, string fromUserId);
        Task<List<FriendRequest>> GetSentRequestsAsync(string fromUserId);
        Task<List<FriendRequest>> GetReceivedRequestsAsync(string toUserId);
        Task<FriendRequest> GetExistingRequestAsync(string fromUserId, string toUserId);
        Task<List<FriendRequest>> GetPendingRequestsAsync(string userId);
        
        // User methods
        Task<User> GetUserByIdAsync(string userId);
        Task<User> UpdateUserAsync(User user);
        Task<List<User>> SearchUsersAsync(string searchTerm);
    }
}
