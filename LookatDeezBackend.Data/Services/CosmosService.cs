using LookatDeezBackend.Data.Models;
using LookatDeezBackend.Data.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using User = LookatDeezBackend.Data.Models.User;

namespace LookatDeezBackend.Data.Services
{
    public class CosmosService : ICosmosService
    {
        private readonly Microsoft.Azure.Cosmos.Container _playlistContainer;
        private readonly Microsoft.Azure.Cosmos.Container _permissionContainer;
        private readonly IFriendRequestRepository _friendRequestRepository;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<CosmosService> _logger;

        public CosmosService(IConfiguration configuration, ILogger<CosmosService> logger)
        {
            _logger = logger;
            
            var connectionString = configuration.GetConnectionString("CosmosDb");
            var databaseName = configuration["CosmosDb_DatabaseName"];
            
            _logger.LogInformation("=== CosmosService Initialization ===" );
            _logger.LogInformation("Connection String: {ConnectionString}", connectionString);
            _logger.LogInformation("Database Name: {DatabaseName}", databaseName);

            var cosmosClient = new CosmosClient(connectionString);
            var database = cosmosClient.GetDatabase(databaseName);

            _playlistContainer = database.GetContainer("playlists");
            _permissionContainer = database.GetContainer("permissions");
            
            _logger.LogInformation("CosmosDB containers initialized: playlists, permissions");
            
            // Initialize repositories
            _friendRequestRepository = new FriendRequestRepository(cosmosClient, databaseName);
            _userRepository = new UserRepository(cosmosClient, databaseName);
            
            _logger.LogInformation("CosmosService initialization completed");
        }

        public async Task<Playlist> CreatePlaylistAsync(Playlist playlist)
        {
            try
            {
                _logger.LogInformation("=== CreatePlaylistAsync ===" );
                _logger.LogInformation("Creating playlist - ID: {PlaylistId}, Title: {Title}, Owner: {OwnerId}", 
                    playlist.Id, playlist.Title, playlist.OwnerId);
                    
                // MUST set id/ownerId before this call
                _logger.LogInformation("Attempting to create playlist in container with partition key: {PartitionKey}", playlist.OwnerId);
                
                var playlistResp = await _playlistContainer.CreateItemAsync(playlist, new PartitionKey(playlist.OwnerId));
                _logger.LogInformation("Playlist created successfully in CosmosDB - ID: {PlaylistId}", playlist.Id);

                // Create owner permission record
                var ownerPermission = new Models.Permission
                {
                    PlaylistId = playlist.Id,
                    UserId = playlist.OwnerId,
                    PermissionLevel = "admin",
                    GrantedBy = playlist.OwnerId, // Owner grants themselves admin permission
                    GrantedAt = DateTime.UtcNow
                };
                
                _logger.LogInformation("Creating owner permission - PlaylistId: {PlaylistId}, UserId: {UserId}", 
                    ownerPermission.PlaylistId, ownerPermission.UserId);

                // Add permission record to permissions container
                await _permissionContainer.CreateItemAsync(ownerPermission, new PartitionKey(ownerPermission.PlaylistId));
                _logger.LogInformation("Owner permission created successfully");

                _logger.LogInformation("CreatePlaylistAsync completed successfully for playlist: {PlaylistId}", playlist.Id);
                return playlistResp.Resource;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "CosmosDB error creating playlist - StatusCode: {StatusCode}, Message: {Message}, PlaylistId: {PlaylistId}", 
                    ex.StatusCode, ex.Message, playlist.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating playlist - PlaylistId: {PlaylistId}, Title: {Title}", 
                    playlist.Id, playlist.Title);
                throw;
            }
        }

        public async Task<Playlist?> GetPlaylistByIdAsync(string id)
        {
            try
            {
                _logger.LogInformation("=== GetPlaylistByIdAsync ===" );
                _logger.LogInformation("Searching for playlist with ID: {PlaylistId}", id);
                
                var q = new Microsoft.Azure.Cosmos.QueryDefinition(
                    "SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id);

                var it = _playlistContainer.GetItemQueryIterator<Playlist>(
                    q,
                    requestOptions: new Microsoft.Azure.Cosmos.QueryRequestOptions { MaxItemCount = 1 } // cross-partition by default
                );
                
                _logger.LogInformation("Executing cross-partition query for playlist ID: {PlaylistId}", id);

                while (it.HasMoreResults)
                {
                    var page = await it.ReadNextAsync();
                    _logger.LogInformation("Query returned {ResultCount} results", page.Count);
                    
                    var doc = page.Resource.FirstOrDefault();
                    if (doc is not null)
                    {
                        _logger.LogInformation("Found playlist - ID: {PlaylistId}, Title: {Title}, Owner: {OwnerId}, ItemCount: {ItemCount}", 
                            doc.Id, doc.Title, doc.OwnerId, doc.Items?.Count ?? 0);
                        return doc;
                    }
                }
                
                _logger.LogWarning("Playlist not found with ID: {PlaylistId}", id);
                return null;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "CosmosDB error getting playlist - StatusCode: {StatusCode}, Message: {Message}, PlaylistId: {PlaylistId}", 
                    ex.StatusCode, ex.Message, id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting playlist - PlaylistId: {PlaylistId}", id);
                throw;
            }
        }


        public async Task<IEnumerable<Playlist>> GetUserPlaylistsAsync(string userId)
        {
            try
            {
                _logger.LogInformation("=== GetUserPlaylistsAsync ===" );
                _logger.LogInformation("Getting playlists owned by user: {UserId}", userId);
                
                var query = _playlistContainer.GetItemLinqQueryable<Playlist>()
                    .Where(p => p.OwnerId == userId)
                    .ToFeedIterator();

                var results = new List<Playlist>();
                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    _logger.LogInformation("Retrieved {PlaylistCount} playlists for user {UserId}", response.Count, userId);
                    results.AddRange(response);
                }
                
                _logger.LogInformation("Total owned playlists for user {UserId}: {TotalCount}", userId, results.Count);
                return results;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "CosmosDB error getting user playlists - StatusCode: {StatusCode}, Message: {Message}, UserId: {UserId}", 
                    ex.StatusCode, ex.Message, userId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting user playlists - UserId: {UserId}", userId);
                throw;
            }
        }

        public async Task<IEnumerable<Playlist>> GetSharedPlaylistsAsync(string userId)
        {
            try
            {
                _logger.LogInformation("=== GetSharedPlaylistsAsync ===" );
                _logger.LogInformation("Getting playlists shared with user: {UserId}", userId);
                
                var permissionQuery = _permissionContainer.GetItemLinqQueryable<Models.Permission>()
                    .Where(p => p.UserId == userId)
                    .ToFeedIterator();

                var playlistIds = new List<string>();
                while (permissionQuery.HasMoreResults)
                {
                    var response = await permissionQuery.ReadNextAsync();
                    var permissions = response.ToList();
                    _logger.LogInformation("Found {PermissionCount} permissions for user {UserId}", permissions.Count, userId);
                    
                    playlistIds.AddRange(permissions.Select(p => p.PlaylistId));
                }
                
                _logger.LogInformation("User {UserId} has permissions for {PlaylistCount} playlists", userId, playlistIds.Count);

                var playlists = new List<Playlist>();
                foreach (var playlistId in playlistIds)
                {
                    _logger.LogInformation("Fetching playlist details for shared playlist: {PlaylistId}", playlistId);
                    var playlist = await GetPlaylistByIdAsync(playlistId);
                    
                    // Only include playlists that are NOT owned by this user (i.e., truly shared)
                    if (playlist != null && playlist.OwnerId != userId)
                    {
                        _logger.LogInformation("Adding shared playlist: {PlaylistId} (owned by {OwnerId})", playlist.Id, playlist.OwnerId);
                        playlists.Add(playlist);
                    }
                    else if (playlist != null)
                    {
                        _logger.LogInformation("Skipping playlist {PlaylistId} - user {UserId} is the owner", playlistId, userId);
                    }
                    else
                    {
                        _logger.LogWarning("Playlist {PlaylistId} not found when fetching shared playlists", playlistId);
                    }
                }
                
                _logger.LogInformation("Total shared playlists for user {UserId}: {TotalCount}", userId, playlists.Count);
                return playlists;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "CosmosDB error getting shared playlists - StatusCode: {StatusCode}, Message: {Message}, UserId: {UserId}", 
                    ex.StatusCode, ex.Message, userId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting shared playlists - UserId: {UserId}", userId);
                throw;
            }
        }

        // In your CosmosService class
        public async Task UpdatePlaylistAsync(Playlist playlist)
        {
            try
            {
                _logger.LogInformation("=== UpdatePlaylistAsync ===" );
                _logger.LogInformation("Updating playlist - ID: {PlaylistId}, Title: {Title}, Owner: {OwnerId}, ItemCount: {ItemCount}", 
                    playlist.Id, playlist.Title, playlist.OwnerId, playlist.Items?.Count ?? 0);
                
                // For playlist container with partition key /ownerId
                await _playlistContainer.ReplaceItemAsync(
                    item: playlist,
                    id: playlist.Id,
                    partitionKey: new PartitionKey(playlist.OwnerId) // This is crucial!
                );
                
                _logger.LogInformation("Playlist updated successfully - ID: {PlaylistId}", playlist.Id);
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "CosmosDB error updating playlist - StatusCode: {StatusCode}, Message: {Message}, PlaylistId: {PlaylistId}", 
                    ex.StatusCode, ex.Message, playlist.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating playlist - PlaylistId: {PlaylistId}", playlist.Id);
                throw;
            }
        }

        // Alternative overload if you want to be more explicit
        public async Task UpdatePlaylistAsync(Playlist playlist, string ownerId)
        {
            try
            {
                await _playlistContainer.ReplaceItemAsync(
                    item: playlist,
                    id: playlist.Id,
                    partitionKey: new PartitionKey(ownerId)
                );
            }
            catch (CosmosException ex)
            {
                throw;
            }
        }
        public async Task DeletePlaylistAsync(string id, string ownerId) // Add ownerId parameter
        {
            // Delete the playlist - partition key is ownerId
            await _playlistContainer.DeleteItemAsync<Playlist>(id, new PartitionKey(ownerId));

            // Delete all permissions - partition key is playlistId  
            var permissions = await GetPlaylistPermissionsAsync(id);
            foreach (var permission in permissions)
            {
                await _permissionContainer.DeleteItemAsync<Models.Permission>(permission.Id, new PartitionKey(permission.PlaylistId));
            }
        }
        public async Task<Models.Permission> CreatePermissionAsync(Models.Permission permission)
        {
            var response = await _permissionContainer.CreateItemAsync(permission, new PartitionKey(permission.PlaylistId));
            return response.Resource;
        }

        public async Task<IEnumerable<Models.Permission>> GetPlaylistPermissionsAsync(string playlistId)
        {
            var query = _permissionContainer.GetItemLinqQueryable<Models.Permission>()
                .Where(p => p.PlaylistId == playlistId)
                .ToFeedIterator();

            var results = new List<Models.Permission>();
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                results.AddRange(response);
            }
            return results;
        }

        public async Task<Models.Permission> GetUserPermissionAsync(string playlistId, string userId)
        {
            var query = _permissionContainer.GetItemLinqQueryable<Models.Permission>()
                .Where(p => p.PlaylistId == playlistId && p.UserId == userId)
                .ToFeedIterator();

            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                return response.FirstOrDefault();
            }
            return null;
        }

        public async Task DeletePermissionAsync(string playlistId, string userId)
        {
            var permission = await GetUserPermissionAsync(playlistId, userId);
            if (permission != null)
            {
                await _permissionContainer.DeleteItemAsync<Models.Permission>(permission.Id, new PartitionKey(permission.PlaylistId));
            }
        }
        
        // Friend-related methods
        public async Task<FriendRequest> CreateFriendRequestAsync(FriendRequest friendRequest)
        {
            return await _friendRequestRepository.CreateFriendRequestAsync(friendRequest);
        }

        public async Task<FriendRequest> GetFriendRequestByIdAsync(string requestId)
        {
            return await _friendRequestRepository.GetFriendRequestByIdAsync(requestId);
        }

        public async Task<FriendRequest> UpdateFriendRequestAsync(FriendRequest friendRequest)
        {
            return await _friendRequestRepository.UpdateFriendRequestAsync(friendRequest);
        }

        public async Task<bool> DeleteFriendRequestAsync(string requestId, string fromUserId)
        {
            return await _friendRequestRepository.DeleteFriendRequestAsync(requestId, fromUserId);
        }

        public async Task<List<FriendRequest>> GetSentRequestsAsync(string fromUserId)
        {
            return await _friendRequestRepository.GetSentRequestsAsync(fromUserId);
        }

        public async Task<List<FriendRequest>> GetReceivedRequestsAsync(string toUserId)
        {
            return await _friendRequestRepository.GetReceivedRequestsAsync(toUserId);
        }

        public async Task<FriendRequest> GetExistingRequestAsync(string fromUserId, string toUserId)
        {
            return await _friendRequestRepository.GetExistingRequestAsync(fromUserId, toUserId);
        }

        public async Task<List<FriendRequest>> GetPendingRequestsAsync(string userId)
        {
            return await _friendRequestRepository.GetPendingRequestsAsync(userId);
        }
        
        // User methods
        public async Task<User> GetUserByIdAsync(string userId)
        {
            return await _userRepository.GetUserByIdAsync(userId);
        }

        public async Task<User> UpdateUserAsync(User user)
        {
            return await _userRepository.UpdateUserAsync(user);
        }

        public async Task<List<User>> SearchUsersAsync(string searchTerm)
        {
            return await _userRepository.SearchUsersAsync(searchTerm);
        }
    }
}
