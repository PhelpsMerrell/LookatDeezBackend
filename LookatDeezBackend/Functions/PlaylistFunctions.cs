using LookatDeezBackend.Data.Models.Requests;
using LookatDeezBackend.Data.Models;
using LookatDeezBackend.Data.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using LookatDeezBackend.Extensions;
using Newtonsoft.Json;
using System.Net;
using System.Text;

namespace LookatDeezBackend.Functions
{
    public class PlaylistFunctions
    {
        private readonly ICosmosService _cosmosService;
        private readonly AuthorizationService _authService;
        private readonly ILogger<PlaylistFunctions> _logger;

        public PlaylistFunctions(ICosmosService cosmosService, AuthorizationService authService, ILogger<PlaylistFunctions> logger)
        {
            _cosmosService = cosmosService;
            _authService = authService;
            _logger = logger;
        }

        [Function("GetPlaylists")]
        public async Task<HttpResponseData> GetPlaylists(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playlists")] HttpRequestData req)
        {
            var userId = req.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorizedResponse;
            }

            var owned = await _cosmosService.GetUserPlaylistsAsync(userId);
            var shared = await _cosmosService.GetSharedPlaylistsAsync(userId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { owned, shared });
            return response;
        }

        [Function("CreatePlaylist")]
        public async Task<HttpResponseData> CreatePlaylist(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playlists")] HttpRequestData req)
        {
            var userId = req.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorizedResponse;
            }

            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonConvert.DeserializeObject<CreatePlaylistRequest>(body);

            if (string.IsNullOrEmpty(request?.Title))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Title is required");
                return badRequestResponse;
            }

            var playlist = new Playlist
            {
                Title = request.Title,
                OwnerId = userId,
                IsPublic = request.IsPublic
            };

            var created = await _cosmosService.CreatePlaylistAsync(playlist);

            var response = req.CreateResponse(HttpStatusCode.Created);
            response.Headers.Add("Location", $"/api/playlists/{created.Id}");
            await response.WriteAsJsonAsync(created);
            return response;
        }

        [Function("GetPlaylist")]
        public async Task<HttpResponseData> GetPlaylist(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playlists/{id}")] HttpRequestData req)
        {
            var userId = req.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorizedResponse;
            }

            var id = req.FunctionContext.BindingContext.BindingData["id"]?.ToString();

            if (!await _authService.CanViewPlaylistAsync(userId, id))
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                return forbiddenResponse;
            }

            var playlist = await _cosmosService.GetPlaylistAsync(id);
            if (playlist == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(playlist);
            return response;
        }

        [Function("DeletePlaylist")]
        public async Task<HttpResponseData> DeletePlaylist(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "playlists/{id}")] HttpRequestData req)
        {
            var userId = req.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorizedResponse;
            }

            var id = req.FunctionContext.BindingContext.BindingData["id"]?.ToString();

            var playlist = await _cosmosService.GetPlaylistAsync(id);
            if (playlist == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                return notFoundResponse;
            }

            if (playlist.OwnerId != userId)
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                return forbiddenResponse;
            }

            await _cosmosService.DeletePlaylistAsync(id);

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }

        [Function("AddItem")]
        public async Task<HttpResponseData> AddItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playlists/{playlistId}/items")] HttpRequestData req)
        {
            var userId = req.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorizedResponse;
            }

            var playlistId = req.FunctionContext.BindingContext.BindingData["playlistId"]?.ToString();

            if (!await _authService.CanEditPlaylistAsync(userId, playlistId))
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                return forbiddenResponse;
            }

            var playlist = await _cosmosService.GetPlaylistAsync(playlistId);
            if (playlist == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                return notFoundResponse;
            }

            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonConvert.DeserializeObject<AddItemRequest>(body);

            if (string.IsNullOrEmpty(request?.Title) || string.IsNullOrEmpty(request?.Url))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Title and URL are required");
                return badRequestResponse;
            }

            var newItem = new PlaylistItem
            {
                Title = request.Title,
                Url = request.Url,
                AddedBy = userId
            };

            playlist.Items.Add(newItem);
            await _cosmosService.UpdatePlaylistAsync(playlist);

            var response = req.CreateResponse(HttpStatusCode.Created);
            response.Headers.Add("Location", $"/api/playlists/{playlistId}/items/{newItem.Id}");
            await response.WriteAsJsonAsync(newItem);
            return response;
        }

        [Function("RemoveItem")]
        public async Task<HttpResponseData> RemoveItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "playlists/{playlistId}/items/{itemId}")] HttpRequestData req)
        {
            var userId = req.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                return unauthorizedResponse;
            }

            var playlistId = req.FunctionContext.BindingContext.BindingData["playlistId"]?.ToString();
            var itemId = req.FunctionContext.BindingContext.BindingData["itemId"]?.ToString();

            if (!await _authService.CanEditPlaylistAsync(userId, playlistId))
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                return forbiddenResponse;
            }

            var playlist = await _cosmosService.GetPlaylistAsync(playlistId);
            if (playlist == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                return notFoundResponse;
            }

            var item = playlist.Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
            {
                var itemNotFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                return itemNotFoundResponse;
            }

            playlist.Items.Remove(item);
            await _cosmosService.UpdatePlaylistAsync(playlist);

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
    }
}