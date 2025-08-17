using LookatDeezBackend.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
