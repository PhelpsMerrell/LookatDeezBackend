using LookatDeezBackend.Data.Models.Requests;
using LookatDeezBackend.Data.Models;
using LookatDeezBackend.Data.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using LookatDeezBackend.Extensions;
using Newtonsoft.Json;
using System.Net;

namespace LookatDeezBackend.Functions
{
    public class PermissionFunctions
    {
        private readonly ICosmosService _cosmosService;
        private readonly AuthorizationService _authService;
        private readonly ILogger<PermissionFunctions> _logger;

        public PermissionFunctions(ICosmosService cosmosService, AuthorizationService authService, ILogger<PermissionFunctions> logger)
        {
            _cosmosService = cosmosService;
            _authService = authService;
            _logger = logger;
        }

        [Function("SharePlaylist")]
        public async Task<HttpResponseData> SharePlaylist(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playlists/{playlistId}/share")] HttpRequestData req)
        {
            var userId = await AuthHelper.GetUserIdAsync(req, _logger);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                return unauthorizedResponse;
            }

            var playlistId = req.FunctionContext.BindingContext.BindingData["playlistId"]?.ToString();

            if (!await _authService.CanManagePermissionsAsync(userId, playlistId))
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenResponse.WriteAsJsonAsync(new { error = "You do not have permission to manage this playlist" });
                return forbiddenResponse;
            }

            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonConvert.DeserializeObject<SharePlaylistRequest>(body);

            if (string.IsNullOrEmpty(request?.UserId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "UserId is required" });
                return badRequestResponse;
            }

            var existing = await _cosmosService.GetUserPermissionAsync(playlistId, request.UserId);
            if (existing != null)
            {
                var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictResponse.WriteAsJsonAsync(new { error = "Permission already exists" });
                return conflictResponse;
            }

            var permission = new Permission
            {
                PlaylistId = playlistId,
                UserId = request.UserId,
                PermissionLevel = request.Permission,
                GrantedBy = userId
            };

            var created = await _cosmosService.CreatePermissionAsync(permission);

            var response = req.CreateResponse(HttpStatusCode.Created);
            response.Headers.Add("Location", $"/api/playlists/{playlistId}/permissions/{request.UserId}");
            await response.WriteAsJsonAsync(created);
            return response;
        }

        [Function("GetPlaylistPermissions")]
        public async Task<HttpResponseData> GetPlaylistPermissions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playlists/{playlistId}/permissions")] HttpRequestData req)
        {
            var userId = await AuthHelper.GetUserIdAsync(req, _logger);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                return unauthorizedResponse;
            }

            var playlistId = req.FunctionContext.BindingContext.BindingData["playlistId"]?.ToString();

            if (!await _authService.CanViewPlaylistAsync(userId, playlistId))
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenResponse.WriteAsJsonAsync(new { error = "You do not have permission to view this playlist" });
                return forbiddenResponse;
            }

            var permissions = await _cosmosService.GetPlaylistPermissionsAsync(playlistId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(permissions);
            return response;
        }

        [Function("RevokeAccess")]
        public async Task<HttpResponseData> RevokeAccess(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "playlists/{playlistId}/permissions/{targetUserId}")] HttpRequestData req)
        {
            var userId = await AuthHelper.GetUserIdAsync(req, _logger);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                return unauthorizedResponse;
            }

            var playlistId = req.FunctionContext.BindingContext.BindingData["playlistId"]?.ToString();
            var targetUserId = req.FunctionContext.BindingContext.BindingData["targetUserId"]?.ToString();

            if (!await _authService.CanManagePermissionsAsync(userId, playlistId))
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenResponse.WriteAsJsonAsync(new { error = "You do not have permission to manage this playlist" });
                return forbiddenResponse;
            }

            await _cosmosService.DeletePermissionAsync(playlistId, targetUserId);

            var response = req.CreateResponse(HttpStatusCode.NoContent);
            return response;
        }
    }
}