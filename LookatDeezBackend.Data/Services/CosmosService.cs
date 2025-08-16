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
            var response = await _playlistContainer.CreateItemAsync(playlist, new PartitionKey(playlist.Id));
            return response.Resource;
        }

        public async Task<Playlist> GetPlaylistAsync(string id)
        {
            try
            {
                var response = await _playlistContainer.ReadItemAsync<Playlist>(id, new PartitionKey(id));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw;
            }
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
                var playlist = await GetPlaylistAsync(playlistId);
                if (playlist != null) playlists.Add(playlist);
            }
            return playlists;
        }

        public async Task<Playlist> UpdatePlaylistAsync(Playlist playlist)
        {
            playlist.UpdatedAt = System.DateTime.UtcNow;
            var response = await _playlistContainer.UpsertItemAsync(playlist, new PartitionKey(playlist.Id));
            return response.Resource;
        }

        public async Task DeletePlaylistAsync(string id)
        {
            await _playlistContainer.DeleteItemAsync<Playlist>(id, new PartitionKey(id));

            // Delete all permissions
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
