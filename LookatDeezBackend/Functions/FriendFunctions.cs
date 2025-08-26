using LookatDeezBackend.Data.Models;
using LookatDeezBackend.Data.Models.Requests;
using LookatDeezBackend.Data.Models.Responses;
using LookatDeezBackend.Data.Services;
using LookatDeezBackend.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System.Net;

namespace LookatDeezBackend.Functions
{
    public class FriendFunctions
    {
        private readonly ILogger<FriendFunctions> _logger;
        private readonly ICosmosService _cosmosService;

        public FriendFunctions(ILogger<FriendFunctions> logger, ICosmosService cosmosService)
        {
            _logger = logger;
            _cosmosService = cosmosService;
        }

        [Function("GetUserFriends")]
        [OpenApiOperation(
            operationId: "GetUserFriends",
            tags: new[] { "Friends" },
            Summary = "Get user's friends list",
            Description = "Returns the list of friends for the specified user."
        )]
        [OpenApiParameter(
            name: "userId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(string),
            Summary = "The user ID"
        )]
        [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(List<FriendResponse>),
            Description = "User's friends retrieved successfully."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NotFound,
            Description = "User not found."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.InternalServerError,
            Description = "An error occurred while retrieving friends."
        )]
        public async Task<HttpResponseData> GetUserFriends(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/friends")] HttpRequestData req,
            string userId,
            FunctionContext context)
        {
            try
            {
                var currentUserId = context.GetUserId();
                var user = await _cosmosService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("User not found");
                    return notFoundResponse;
                }

                var friends = new List<FriendResponse>();
                foreach (var friendId in user.Friends)
                {
                    var friend = await _cosmosService.GetUserByIdAsync(friendId);
                    if (friend != null)
                    {
                        friends.Add(new FriendResponse
                        {
                            Id = friend.Id,
                            DisplayName = friend.DisplayName,
                            Email = friend.Email,
                            FriendsSince = friend.CreatedAt
                        });
                    }
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(friends);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user friends for user {UserId}", userId);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving friends");
                return errorResponse;
            }
        }

        [Function("SendFriendRequest")]
        [OpenApiOperation(
            operationId: "SendFriendRequest",
            tags: new[] { "Friends" },
            Summary = "Send a friend request",
            Description = "Sends a friend request from the authenticated user to another user."
        )]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(CreateFriendRequestRequest),
            Required = true,
            Description = "The friend request details"
        )]
        [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Created,
            contentType: "application/json",
            bodyType: typeof(FriendRequestResponse),
            Description = "Friend request sent successfully."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.BadRequest,
            Description = "Invalid request data or users are already friends."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.NotFound,
            Description = "Target user not found."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.InternalServerError,
            Description = "An error occurred while sending the friend request."
        )]
        public async Task<HttpResponseData> SendFriendRequest(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "friend-requests")] HttpRequestData req,
            FunctionContext context)
        {
            try
            {
                var currentUserId = context.GetUserId();
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var createRequest = JsonConvert.DeserializeObject<CreateFriendRequestRequest>(requestBody);

                if (createRequest == null || string.IsNullOrEmpty(createRequest.ToUserId))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("ToUserId is required");
                    return badRequestResponse;
                }

                if (currentUserId == createRequest.ToUserId)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Cannot send friend request to yourself");
                    return badRequestResponse;
                }

                var targetUser = await _cosmosService.GetUserByIdAsync(createRequest.ToUserId);
                if (targetUser == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Target user not found");
                    return notFoundResponse;
                }

                var currentUser = await _cosmosService.GetUserByIdAsync(currentUserId);
                if (currentUser?.Friends?.Contains(createRequest.ToUserId) == true)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Users are already friends");
                    return badRequestResponse;
                }

                var existingRequest = await _cosmosService.GetExistingRequestAsync(currentUserId, createRequest.ToUserId);
                var reverseRequest = await _cosmosService.GetExistingRequestAsync(createRequest.ToUserId, currentUserId);

                if (existingRequest?.Status == FriendRequestStatus.Pending)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Friend request already sent");
                    return badRequestResponse;
                }

                if (reverseRequest?.Status == FriendRequestStatus.Pending)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("This user has already sent you a friend request");
                    return badRequestResponse;
                }

                var friendRequest = new FriendRequest
                {
                    FromUserId = currentUserId,
                    ToUserId = createRequest.ToUserId,
                    Status = FriendRequestStatus.Pending
                };

                var createdRequest = await _cosmosService.CreateFriendRequestAsync(friendRequest);

                var responseData = new FriendRequestResponse
                {
                    Id = createdRequest.Id,
                    FromUserId = createdRequest.FromUserId,
                    FromUserDisplayName = currentUser?.DisplayName ?? "",
                    ToUserId = createdRequest.ToUserId,
                    ToUserDisplayName = targetUser.DisplayName,
                    Status = createdRequest.Status,
                    RequestedAt = createdRequest.RequestedAt,
                    RespondedAt = createdRequest.RespondedAt
                };

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(responseData);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending friend request");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while sending friend request");
                return errorResponse;
            }
        }

        [Function("GetFriendRequests")]
        [OpenApiOperation(operationId: "GetFriendRequests", tags: ["Friends"])]
        [OpenApiParameter(name: "x-user-id", In = ParameterLocation.Header, Required = true, Type = typeof(string))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(FriendRequestsEnvelope))]
        public async Task<HttpResponseData> GetFriendRequests(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "friend-requests")] HttpRequestData req)
        {
            try
            {
                var currentUserId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(currentUserId))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                // Get sent and received requests
                var sentRequests = await _cosmosService.GetSentRequestsAsync(currentUserId);
                var receivedRequests = await _cosmosService.GetReceivedRequestsAsync(currentUserId);

                // Build response with user details
                var envelope = new FriendRequestsEnvelope();

                foreach (var request in sentRequests)
                {
                    var toUser = await _cosmosService.GetUserByIdAsync(request.ToUserId);
                    envelope.Sent.Add(new FriendRequestResponse
                    {
                        Id = request.Id,
                        FromUserId = request.FromUserId,
                        FromUserDisplayName = "", // Current user
                        ToUserId = request.ToUserId,
                        ToUserDisplayName = toUser?.DisplayName ?? "",
                        Status = request.Status,
                        RequestedAt = request.RequestedAt,
                        RespondedAt = request.RespondedAt
                    });
                }

                foreach (var request in receivedRequests)
                {
                    var fromUser = await _cosmosService.GetUserByIdAsync(request.FromUserId);
                    envelope.Received.Add(new FriendRequestResponse
                    {
                        Id = request.Id,
                        FromUserId = request.FromUserId,
                        FromUserDisplayName = fromUser?.DisplayName ?? "",
                        ToUserId = request.ToUserId,
                        ToUserDisplayName = "", // Current user
                        Status = request.Status,
                        RequestedAt = request.RequestedAt,
                        RespondedAt = request.RespondedAt
                    });
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(envelope);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting friend requests for user {UserId}", await AuthHelper.GetUserIdAsync(req, _logger));
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while retrieving friend requests");
                return errorResponse;
            }
        }

        [Function("UpdateFriendRequest")]
        [OpenApiOperation(operationId: "UpdateFriendRequest", tags: ["Friends"])]
        [OpenApiParameter(name: "requestId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
        [OpenApiParameter(name: "x-user-id", In = ParameterLocation.Header, Required = true, Type = typeof(string))]
        [OpenApiRequestBody("application/json", typeof(UpdateFriendRequestRequest))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(FriendRequestResponse))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(object))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object))]
        public async Task<HttpResponseData> UpdateFriendRequest(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "friend-requests/{requestId}")] HttpRequestData req,
            string requestId)
        {
            try
            {
                var currentUserId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(currentUserId))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var updateRequest = JsonConvert.DeserializeObject<UpdateFriendRequestRequest>(requestBody);

                if (updateRequest == null)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Invalid request body");
                    return badRequestResponse;
                }

                // Get the friend request
                var friendRequest = await _cosmosService.GetFriendRequestByIdAsync(requestId);
                if (friendRequest == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Friend request not found");
                    return notFoundResponse;
                }

                // Only the recipient can accept/decline
                if (friendRequest.ToUserId != currentUserId)
                {
                    var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbiddenResponse.WriteStringAsync("You can only respond to friend requests sent to you");
                    return forbiddenResponse;
                }

                // Can only update pending requests
                if (friendRequest.Status != FriendRequestStatus.Pending)
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Friend request has already been responded to");
                    return badRequestResponse;
                }

                // Update request status
                friendRequest.Status = updateRequest.Status;
                friendRequest.RespondedAt = DateTime.UtcNow;

                var updatedRequest = await _cosmosService.UpdateFriendRequestAsync(friendRequest);

                // If accepted, add each other as friends
                if (updateRequest.Status == FriendRequestStatus.Accepted)
                {
                    var fromUser = await _cosmosService.GetUserByIdAsync(friendRequest.FromUserId);
                    var toUser = await _cosmosService.GetUserByIdAsync(friendRequest.ToUserId);

                    if (fromUser != null && toUser != null)
                    {
                        // Add to each other's friends list
                        if (!fromUser.Friends.Contains(toUser.Id))
                        {
                            fromUser.Friends.Add(toUser.Id);
                            await _cosmosService.UpdateUserAsync(fromUser);
                        }

                        if (!toUser.Friends.Contains(fromUser.Id))
                        {
                            toUser.Friends.Add(fromUser.Id);
                            await _cosmosService.UpdateUserAsync(toUser);
                        }
                    }
                }

                // Build response
                var fromUserDetails = await _cosmosService.GetUserByIdAsync(friendRequest.FromUserId);
                var toUserDetails = await _cosmosService.GetUserByIdAsync(friendRequest.ToUserId);

                var responseData = new FriendRequestResponse
                {
                    Id = updatedRequest.Id,
                    FromUserId = updatedRequest.FromUserId,
                    FromUserDisplayName = fromUserDetails?.DisplayName ?? "",
                    ToUserId = updatedRequest.ToUserId,
                    ToUserDisplayName = toUserDetails?.DisplayName ?? "",
                    Status = updatedRequest.Status,
                    RequestedAt = updatedRequest.RequestedAt,
                    RespondedAt = updatedRequest.RespondedAt
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(responseData);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating friend request {RequestId}", requestId);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while updating friend request");
                return errorResponse;
            }
        }

        [Function("RemoveFriend")]
        [OpenApiOperation(operationId: "RemoveFriend", tags: ["Friends"])]
        [OpenApiParameter(name: "friendId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
        [OpenApiParameter(name: "x-user-id", In = ParameterLocation.Header, Required = true, Type = typeof(string))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NoContent, contentType: "application/json", bodyType: typeof(object))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object))]
        public async Task<HttpResponseData> RemoveFriend(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "friends/{friendId}")] HttpRequestData req,
            string friendId)
        {
            try
            {
                var currentUserId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(currentUserId))
                {
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                // Get both users
                var currentUser = await _cosmosService.GetUserByIdAsync(currentUserId);
                var friendUser = await _cosmosService.GetUserByIdAsync(friendId);

                if (currentUser == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Current user not found");
                    return notFoundResponse;
                }

                if (friendUser == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync("Friend not found");
                    return notFoundResponse;
                }

                // Check if they are actually friends
                if (!currentUser.Friends.Contains(friendId))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteStringAsync("Users are not friends");
                    return badRequestResponse;
                }

                // Remove from each other's friends list
                currentUser.Friends.Remove(friendId);
                friendUser.Friends.Remove(currentUserId);

                // Update both users
                await _cosmosService.UpdateUserAsync(currentUser);
                await _cosmosService.UpdateUserAsync(friendUser);

                var response = req.CreateResponse(HttpStatusCode.NoContent);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing friend {FriendId} for user {UserId}", friendId, await AuthHelper.GetUserIdAsync(req, _logger));
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while removing friend");
                return errorResponse;
            }
        }
    }
}
