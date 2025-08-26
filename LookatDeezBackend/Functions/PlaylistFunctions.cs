using LookatDeezBackend.Data.Models.Requests;
using LookatDeezBackend.Data.Models;
using LookatDeezBackend.Data.Services;
using LookatDeezBackend.Extensions;
using LookatDeezBackend.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;
using Microsoft.Azure.Cosmos;
using System.Text.Json;



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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playlists")] HttpRequestData req,
            FunctionContext context)
        {
            try
            {
                var userId = context.GetUserId();
                _logger.LogInformation("Getting playlists for user: {UserId}", userId);
                
                var owned = await _cosmosService.GetUserPlaylistsAsync(userId);
                var shared = await _cosmosService.GetSharedPlaylistsAsync(userId);
                
                _logger.LogInformation("Found {OwnedCount} owned and {SharedCount} shared playlists", owned.Count(), shared.Count());

                var response = CorsHelper.CreateCorsResponse(req, HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { owned, shared });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting playlists");
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "Failed to get playlists", details = ex.Message });
                return errorResponse;
            }
        }
        public sealed class PlaylistsEnvelope
        {
            public IEnumerable<PlaylistSummary> owned { get; set; } = Array.Empty<PlaylistSummary>();
            public IEnumerable<PlaylistSummary> shared { get; set; } = Array.Empty<PlaylistSummary>();
        }

        public sealed class PlaylistSummary
        {
            public string Id { get; set; } = default!;
            public string Title { get; set; } = default!;
            public int ItemCount { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }


        [Function("CreatePlaylist")]
        public async Task<HttpResponseData> CreatePlaylist(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "playlists")] HttpRequestData req,
            FunctionContext context)
        {
            try
            {
                var userId = context.GetUserId();
                _logger.LogInformation("Creating playlist for user: {UserId}", userId);
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation("Request body: {Body}", body);
                
                var request = JsonConvert.DeserializeObject<CreatePlaylistRequest>(body);

                if (string.IsNullOrEmpty(request?.Title))
                {
                    _logger.LogWarning("Empty title in playlist creation request");
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { error = "Title is required" });
                    return badRequestResponse;
                }

                var now = DateTime.UtcNow;
                var playlist = new Playlist
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Title = request.Title.Trim(),
                    OwnerId = userId,
                    IsPublic = request.IsPublic,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                
                _logger.LogInformation("Attempting to create playlist: {Title} for owner: {OwnerId}", playlist.Title, playlist.OwnerId);

                var created = await _cosmosService.CreatePlaylistAsync(playlist);
                
                var response = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Created);
                response.Headers.Add("Location", $"/api/playlists/{created.Id}");
                await response.WriteAsJsonAsync(created);
                
                _logger.LogInformation("Playlist created successfully: {PlaylistId}", created.Id);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating playlist");
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "Failed to create playlist", details = ex.Message });
                return errorResponse;
            }
        }

        [Function("GetPlaylist")]
        [OpenApiOperation(
            operationId: "GetPlaylist",
            tags: new[] { "Playlists" },
            Summary = "Get a playlist by id",
            Description = "Returns the playlist if the caller has view permission (owner or collaborator)."
        )]
        [OpenApiParameter(
            name: "id",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(string),
            Summary = "The playlist id"
        )]
        [OpenApiParameter(
            name: "x-user-id",
            In = ParameterLocation.Header,
            Required = true,
            Type = typeof(string),
            Summary = "Dev-only user id header (replace with Azure AD B2C later)."
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(Playlist),
            Description = "The playlist."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Unauthorized,
            Description = "Missing x-user-id."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Forbidden,
            Description = "Caller does not have permission to view the playlist."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NotFound,
            Description = "Playlist not found."
        )]

        
        public async Task<HttpResponseData> GetPlaylist(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "playlists/{id}")]
    HttpRequestData req,
    string id,
    FunctionContext context)
        {
            var userId = context.GetUserId();
            var canView = await _authService.CanViewPlaylistAsync(userId, id);
            if (!canView)
                return req.CreateResponse(HttpStatusCode.Forbidden);

            var playlist = await _cosmosService.GetPlaylistByIdAsync(id);
            if (playlist is null)
                return req.CreateResponse(HttpStatusCode.NotFound);

            if (playlist.Items != null && playlist.Items.Any())
            {
                playlist.Items = playlist.Items.OrderBy(item => item.Order).ToList();
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(playlist, new() {  });
            return res;
        }

        [Function("DeletePlaylist")]
        [OpenApiOperation(
            operationId: "DeletePlaylist",
            tags: new[] { "Playlists" },
            Summary = "Delete a playlist by id",
            Description = "Deletes the playlist if the caller is the owner."
        )]
        [OpenApiParameter(
            name: "id",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(string),
            Summary = "The playlist id"
        )]
        [OpenApiParameter(
            name: "x-user-id",
            In = ParameterLocation.Header,
            Required = true,
            Type = typeof(string),
            Summary = "Dev-only user id header (replace with Azure AD B2C later)."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NoContent,
            Description = "Playlist deleted successfully."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Unauthorized,
            Description = "Missing x-user-id."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Forbidden,
            Description = "Caller is not the owner of the playlist."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NotFound,
            Description = "Playlist not found."
        )]
        public async Task<HttpResponseData> DeletePlaylist(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "playlists/{id}")] HttpRequestData req,
            string id)
        {
            var userId = await AuthHelper.GetUserIdAsync(req, _logger);
            if (string.IsNullOrEmpty(userId))
            {
                var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                return unauthorizedResponse;
            }

            try
            {
                // Add some basic logging/debugging
                Console.WriteLine($"Attempting to get playlist with ID: {id}");

                var playlist = await _cosmosService.GetPlaylistByIdAsync(id);
                if (playlist == null)
                {
                    Console.WriteLine($"Playlist not found: {id}");
                    return req.CreateResponse(HttpStatusCode.NotFound);
                }

                Console.WriteLine($"Found playlist: {playlist.Id}, Owner: {playlist.OwnerId}");

                if (playlist.OwnerId != userId)
                {
                    Console.WriteLine($"Permission denied. Playlist owner: {playlist.OwnerId}, Request user: {userId}");
                    return req.CreateResponse(HttpStatusCode.Forbidden);
                }

                Console.WriteLine($"Attempting to delete playlist: {id} with owner: {playlist.OwnerId}");
                await _cosmosService.DeletePlaylistAsync(id, playlist.OwnerId); // Pass both id and ownerId

                Console.WriteLine($"Successfully deleted playlist: {id}");
                return req.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                // Log the exception here if you have logging
                // _logger.LogError(ex, "Error deleting playlist {PlaylistId}", id);
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }
        }


        [Function("AddItem")]
        [OpenApiOperation(
            operationId: "AddItem",
            tags: new[] { "Playlists" },
            Summary = "Add an item to a playlist",
            Description = "Adds a new item to the specified playlist if the caller is the owner."
        )]
        [OpenApiParameter(
            name: "playlistId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(string),
            Summary = "The playlist id"
        )]
        [OpenApiParameter(
            name: "x-user-id",
            In = ParameterLocation.Header,
            Required = true,
            Type = typeof(string),
            Summary = "Dev-only user id header (replace with Azure AD B2C later)."
        )]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(AddItemRequest),
            Required = true,
            Description = "The item details to add to the playlist"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Created,
            contentType: "application/json",
            bodyType: typeof(PlaylistItem),
            Description = "Item added successfully."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.BadRequest,
            Description = "Invalid request data."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Unauthorized,
            Description = "User not authenticated."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Forbidden,
            Description = "User is not the owner of the playlist."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NotFound,
            Description = "Playlist not found."
        )]
        public async Task<HttpResponseData> AddItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "playlists/{playlistId}/items")] HttpRequestData req,
            string playlistId,
            FunctionContext context)
        {
            // Handle CORS preflight requests
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return await CorsHelper.HandlePreflightRequest(req);
            }

            try
            {
                // Add debug logging
                _logger.LogInformation($"AddItem called for playlist: {playlistId}");
                
                // Get user ID from context (set by middleware)
                var userId = context.GetUserId();
                _logger.LogInformation($"User ID from context: {userId}");

                // Validate playlist ID
                if (string.IsNullOrWhiteSpace(playlistId))
                {
                    _logger.LogWarning("Empty playlist ID");
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Playlist ID is required");
                    return badRequestResponse;
                }

                // Parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"Request body: {requestBody}");
                
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    _logger.LogWarning("Empty request body");
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Request body is required");
                    return badRequestResponse;
                }

                AddItemRequest addItemRequest;
                try
                {
                    addItemRequest = System.Text.Json.JsonSerializer.Deserialize<AddItemRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (System.Text.Json.JsonException ex)
                {
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync($"Invalid JSON format: {ex.Message}");
                    return badRequestResponse;
                }

                // Validate request data
                if (addItemRequest == null)
                {
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Request data is required");
                    return badRequestResponse;
                }

                if (string.IsNullOrWhiteSpace(addItemRequest.Title))
                {
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Title is required");
                    return badRequestResponse;
                }

                if (string.IsNullOrWhiteSpace(addItemRequest.Url))
                {
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("URL is required");
                    return badRequestResponse;
                }

                // Validate URL format
                if (!Uri.TryCreate(addItemRequest.Url, UriKind.Absolute, out _))
                {
                    var badRequestResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid URL format");
                    return badRequestResponse;
                }

                // Get playlist from service
                var playlist = await _cosmosService.GetPlaylistByIdAsync(playlistId);
                if (playlist == null)
                {
                    var notFoundResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Playlist not found");
                    return notFoundResponse;
                }

                // Check if user is the owner
                if (playlist.OwnerId != userId)
                {
                    var forbiddenResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Forbidden);
                    await forbiddenResponse.WriteStringAsync("You are not the owner of this playlist");
                    return forbiddenResponse;
                }

                // Create new playlist item
                var newItem = new PlaylistItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = addItemRequest.Title.Trim(),
                    Url = addItemRequest.Url.Trim(),
                    AddedAt = DateTime.UtcNow,
                    AddedBy = userId,
                    Order = playlist.Items?.Count ?? 0 // Auto-assign next order
                };

                // Add item to playlist
                if (playlist.Items == null)
                {
                    playlist.Items = new List<PlaylistItem>();
                }
                playlist.Items.Add(newItem);

                // Update playlist in database
                await _cosmosService.UpdatePlaylistAsync(playlist);

                // Create success response
                var response = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Created);

                // Set Location header
                var baseUrl = $"{req.Url.Scheme}://{req.Url.Host}";
                if (req.Url.Port != 80 && req.Url.Port != 443)
                {
                    baseUrl += $":{req.Url.Port}";
                }
                var locationUrl = $"{baseUrl}/api/playlists/{playlistId}/items/{newItem.Id}";
                response.Headers.Add("Location", locationUrl);

                // Set response content
                await response.WriteAsJsonAsync(newItem);
                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                var notFoundResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Playlist not found");
                return notFoundResponse;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Database error while adding item to playlist {PlaylistId}", playlistId);
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Database error: {ex.Message}");
                return errorResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while adding item to playlist {PlaylistId}", playlistId);
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"An unexpected error occurred: {ex.Message}");
                return errorResponse;
            }
        }


        [Function("RemoveItem")]
        [OpenApiOperation(
    operationId: "RemoveItem",
    tags: new[] { "Playlists" },
    Summary = "Remove an item from a playlist",
    Description = "Removes the specified item from the playlist if the caller has edit permissions."
)]
        [OpenApiParameter(
    name: "playlistId",
    In = ParameterLocation.Path,
    Required = true,
    Type = typeof(string),
    Summary = "The playlist id"
)]
        [OpenApiParameter(
    name: "itemId",
    In = ParameterLocation.Path,
    Required = true,
    Type = typeof(string),
    Summary = "The item id to remove"
)]
        [OpenApiParameter(
    name: "x-user-id",
    In = ParameterLocation.Header,
    Required = true,
    Type = typeof(string),
    Summary = "Dev-only user id header (replace with Azure AD B2C later)."
)]
        [OpenApiResponseWithoutBody(
    statusCode: HttpStatusCode.NoContent,
    Description = "Item removed successfully."
)]
        [OpenApiResponseWithoutBody(
    statusCode: HttpStatusCode.Unauthorized,
    Description = "User not authenticated."
)]
        [OpenApiResponseWithoutBody(
    statusCode: HttpStatusCode.Forbidden,
    Description = "User does not have permission to edit this playlist."
)]
        [OpenApiResponseWithoutBody(
    statusCode: HttpStatusCode.NotFound,
    Description = "Playlist or item not found."
)]
        public async Task<HttpResponseData> RemoveItem(
    [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "playlists/{playlistId}/items/{itemId}")] HttpRequestData req,
    string playlistId,
    string itemId)
        {
            try
            {
                // Get user ID from JWT token
                var userId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(userId))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                // Validate path parameters
                if (string.IsNullOrWhiteSpace(playlistId))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Playlist ID is required");
                    return badRequestResponse;
                }

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Item ID is required");
                    return badRequestResponse;
                }

                // Check permissions
                if (!await _authService.CanEditPlaylistAsync(userId, playlistId))
                {
                    var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbiddenResponse.WriteStringAsync("You do not have permission to edit this playlist");
                    return forbiddenResponse;
                }

                // Get playlist
                var playlist = await _cosmosService.GetPlaylistByIdAsync(playlistId);
                if (playlist == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Playlist not found");
                    return notFoundResponse;
                }

                // Check if playlist has items
                if (playlist.Items == null || !playlist.Items.Any())
                {
                    var itemNotFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await itemNotFoundResponse.WriteStringAsync("Item not found");
                    return itemNotFoundResponse;
                }

                // Find and remove the item
                var item = playlist.Items.FirstOrDefault(i => i.Id == itemId);
                if (item == null)
                {
                    var itemNotFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await itemNotFoundResponse.WriteStringAsync("Item not found");
                    return itemNotFoundResponse;
                }

                playlist.Items.Remove(item);

                // Update playlist in database
                await _cosmosService.UpdatePlaylistAsync(playlist);

                // Return success response
                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Playlist not found");
                return notFoundResponse;
            }
            catch (CosmosException ex)
            {
                // Log the exception details (add your logging here)
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Database error: {ex.Message}");
                return errorResponse;
            }
            catch (Exception ex)
            {
                // Log the exception details (add your logging here)
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"An unexpected error occurred: {ex.Message}");
                return errorResponse;
            }
        }

        [Function("ReorderItems")]
        [OpenApiOperation(
            operationId: "ReorderItems",
            tags: new[] { "Playlists" },
            Summary = "Reorder items in a playlist",
            Description = "Updates the order of items in the specified playlist if the caller has edit permissions."
        )]
        [OpenApiParameter(
            name: "playlistId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(string),
            Summary = "The playlist id"
        )]
        [OpenApiParameter(
            name: "x-user-id",
            In = ParameterLocation.Header,
            Required = true,
            Type = typeof(string),
            Summary = "Dev-only user id header (replace with Azure AD B2C later)."
        )]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(ReorderItemsRequest),
            Required = true,
            Description = "The new order of items as an array of item IDs"
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NoContent,
            Description = "Items reordered successfully."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.BadRequest,
            Description = "Invalid request data."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Unauthorized,
            Description = "User not authenticated."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Forbidden,
            Description = "User does not have permission to edit this playlist."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NotFound,
            Description = "Playlist not found."
        )]
        public async Task<HttpResponseData> ReorderItems(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "playlists/{playlistId}/items/order")] HttpRequestData req,
            string playlistId)
        {
            try
            {
                // Get user ID from JWT token
                var userId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(userId))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                // Validate playlist ID
                if (string.IsNullOrWhiteSpace(playlistId))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Playlist ID is required");
                    return badRequestResponse;
                }

                // Parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Request body is required");
                    return badRequestResponse;
                }

                ReorderItemsRequest reorderRequest;
                try
                {
                    reorderRequest = System.Text.Json.JsonSerializer.Deserialize<ReorderItemsRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (System.Text.Json.JsonException ex)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync($"Invalid JSON format: {ex.Message}");
                    return badRequestResponse;
                }

                // Validate request data
                if (reorderRequest == null || reorderRequest.ItemOrder == null || !reorderRequest.ItemOrder.Any())
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Item order array is required");
                    return badRequestResponse;
                }

                // Check permissions
                if (!await _authService.CanEditPlaylistAsync(userId, playlistId))
                {
                    var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbiddenResponse.WriteStringAsync("You do not have permission to edit this playlist");
                    return forbiddenResponse;
                }

                // Get playlist
                var playlist = await _cosmosService.GetPlaylistByIdAsync(playlistId);
                if (playlist == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Playlist not found");
                    return notFoundResponse;
                }

                // Validate that all item IDs exist in the playlist
                if (playlist.Items == null || !playlist.Items.Any())
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Playlist has no items to reorder");
                    return badRequestResponse;
                }

                var existingItemIds = playlist.Items.Select(i => i.Id).ToHashSet();
                var requestedItemIds = reorderRequest.ItemOrder.ToHashSet();

                if (!existingItemIds.SetEquals(requestedItemIds))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Item order must contain all existing item IDs");
                    return badRequestResponse;
                }

                // Update the order of items
                var itemLookup = playlist.Items.ToDictionary(i => i.Id);
                for (int i = 0; i < reorderRequest.ItemOrder.Count; i++)
                {
                    var itemId = reorderRequest.ItemOrder[i];
                    if (itemLookup.TryGetValue(itemId, out var item))
                    {
                        item.Order = i;
                    }
                }

                // Sort items by new order
                playlist.Items = playlist.Items.OrderBy(i => i.Order).ToList();

                // Update timestamp
                playlist.UpdatedAt = DateTime.UtcNow;

                // Save playlist
                await _cosmosService.UpdatePlaylistAsync(playlist);

                // Return success
                return req.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Playlist not found");
                return notFoundResponse;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Database error while reordering items for playlist {PlaylistId}", playlistId);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Database error: {ex.Message}");
                return errorResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while reordering items for playlist {PlaylistId}", playlistId);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"An unexpected error occurred: {ex.Message}");
                return errorResponse;
            }
        }

    }
}