using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace LookatDeezBackend.Data.Services
{
    public class AuthorizationService
    {
        private readonly ICosmosService _cosmosService;

        public AuthorizationService(ICosmosService cosmosService)
        {
            _cosmosService = cosmosService;
        }
      
        public async Task<bool> CanViewPlaylistAsync(string userId, string playlistId)
        {
            var playlist = await _cosmosService.GetPlaylistByIdAsync(playlistId);
            if (playlist == null) return false;

            if (playlist.OwnerId == userId) return true;
            if (playlist.IsPublic) return true;

            var permission = await _cosmosService.GetUserPermissionAsync(playlistId, userId);
            return permission != null;
        }

        public async Task<bool> CanEditPlaylistAsync(string userId, string playlistId)
        {
            var playlist = await _cosmosService.GetPlaylistByIdAsync(playlistId);
            if (playlist == null) return false;

            if (playlist.OwnerId == userId) return true;

            var permission = await _cosmosService.GetUserPermissionAsync(playlistId, userId);
            return permission != null && (permission.PermissionLevel == "edit" || permission.PermissionLevel == "admin");
        }

        public async Task<bool> CanManagePermissionsAsync(string userId, string playlistId)
        {
            var playlist = await _cosmosService.GetPlaylistByIdAsync(playlistId);
            if (playlist == null) return false;

            if (playlist.OwnerId == userId) return true;

            var permission = await _cosmosService.GetUserPermissionAsync(playlistId, userId);
            return permission != null && permission.PermissionLevel == "admin";
        }
    }
}


