using LookatDeezBackend.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LookatDeezBackend.Data.Services
{
    public class CosmosService : ICosmosService
    {
        private readonly Microsoft.Azure.Cosmos.Container _playlistContainer;
        private readonly Microsoft.Azure.Cosmos.Container _permissionContainer;

        public CosmosService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("CosmosDb");
            var databaseName = configuration["CosmosDb:DatabaseName"];

            var cosmosClient = new CosmosClient(connectionString);
            var database = cosmosClient.GetDatabase(databaseName);

            _playlistContainer = database.GetContainer("playlists");
            _permissionContainer = database.GetContainer("permissions");
        }

        public async Task<Playlist> CreatePlaylistAsync(Playlist playlist)
        {
            // MUST set id/ownerId before this call
            var playlistResp = await _playlistContainer.CreateItemAsync(playlist, new PartitionKey(playlist.OwnerId));

            // Create owner permission record
            var ownerPermission = new Models.Permission
            {
                PlaylistId = playlist.Id,
                UserId = playlist.OwnerId,
                PermissionLevel = "admin",
                GrantedBy = playlist.OwnerId, // Owner grants themselves admin permission
                GrantedAt = DateTime.UtcNow
            };

            // Add permission record to permissions container
            await _permissionContainer.CreateItemAsync(ownerPermission, new PartitionKey(ownerPermission.PlaylistId));

            return playlistResp.Resource;
        }

        public async Task<Playlist?> GetPlaylistByIdAsync(string id)
        {
            var q = new Microsoft.Azure.Cosmos.QueryDefinition(
                "SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id);

            var it = _playlistContainer.GetItemQueryIterator<Playlist>(
                q,
                requestOptions: new Microsoft.Azure.Cosmos.QueryRequestOptions { MaxItemCount = 1 } // cross-partition by default
            );

            while (it.HasMoreResults)
            {
                var page = await it.ReadNextAsync();
                var doc = page.Resource.FirstOrDefault();
                if (doc is not null) return doc;
            }
            return null;
        }


        public async Task<IEnumerable<Playlist>> GetUserPlaylistsAsync(string userId)
        {
            var query = _playlistContainer.GetItemLinqQueryable<Playlist>()
                .Where(p => p.OwnerId == userId)
                .ToFeedIterator();

            var results = new List<Playlist>();
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                results.AddRange(response);
            }
            return results;
        }

        public async Task<IEnumerable<Playlist>> GetSharedPlaylistsAsync(string userId)
        {
            var permissionQuery = _permissionContainer.GetItemLinqQueryable<Models.Permission>()
                .Where(p => p.UserId == userId)
                .ToFeedIterator();

            var playlistIds = new List<string>();
            while (permissionQuery.HasMoreResults)
            {
                var response = await permissionQuery.ReadNextAsync();
                playlistIds.AddRange(response.Select(p => p.PlaylistId));
            }

            var playlists = new List<Playlist>();
            foreach (var playlistId in playlistIds)
            {
                var playlist = await GetPlaylistByIdAsync(playlistId);
                // Only include playlists that are NOT owned by this user (i.e., truly shared)
                if (playlist != null && playlist.OwnerId != userId)
                {
                    playlists.Add(playlist);
                }
            }
            return playlists;
        }

        // In your CosmosService class
        public async Task UpdatePlaylistAsync(Playlist playlist)
        {
            try
            {
                // For playlist container with partition key /ownerId
                await _playlistContainer.ReplaceItemAsync(
                    item: playlist,
                    id: playlist.Id,
                    partitionKey: new PartitionKey(playlist.OwnerId) // This is crucial!
                );
            }
            catch (CosmosException ex)
            {
                // Log and rethrow
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
    }
}
