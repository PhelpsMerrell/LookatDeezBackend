using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using LookatDeezBackend.Data.Models;
using LookatDeezBackend.Data.Repositories;
using LookatDeezBackend.Extensions;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Web;
using SystemTextJson = System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using System.Security.Claims;

namespace LookatDeezBackend.Functions
{
    public class UserFunctions
    {
        private readonly IUserRepository _userRepository;
        private readonly ILogger<UserFunctions> _logger;

        public UserFunctions(IUserRepository userRepository, ILogger<UserFunctions> logger)
        {
            _userRepository = userRepository;
            _logger = logger;
        }

        [Function("CreateUser")]
        [OpenApiOperation(
            operationId: "createUser",
            tags: new[] { "Users" },
            Summary = "Create a new user",
            Description = "Creates a new user account with JWT authentication required."
        )]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(CreateUserDto),
            Required = true,
            Description = "User creation data"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Created,
            contentType: "application/json",
            bodyType: typeof(User),
            Summary = "User created successfully",
            Description = "Returns the created user object"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.BadRequest,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Invalid request",
            Description = "The request body is invalid or missing required fields"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Unauthorized,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Unauthorized",
            Description = "Valid JWT token required"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Conflict,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Email already exists",
            Description = "A user with this email address already exists"
        )]
        public async Task<HttpResponseData> CreateUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Creating new user with JWT validation");

                // Validate JWT token
                var principal = await AuthHelper.ValidateTokenAsync(req, _logger);
                if (principal == null)
                {
                    _logger.LogWarning("Unauthorized: Invalid or missing JWT token");
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                var microsoftUserId = AuthHelper.GetUserIdFromPrincipal(principal, _logger);
                if (string.IsNullOrEmpty(microsoftUserId))
                {
                    _logger.LogWarning("No user ID found in JWT token");
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Invalid JWT token - no user ID" });
                    return unauthorizedResponse;
                }

                // Extract user info from JWT
                var email = principal.Claims.FirstOrDefault(c => c.Type == "email" || c.Type == "emails")?.Value;
                var displayName = principal.Claims.FirstOrDefault(c => c.Type == "name")?.Value;

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(displayName))
                {
                    // Fall back to request body if claims missing
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(requestBody))
                    {
                        try
                        {
                            var createUserDto = SystemTextJson.JsonSerializer.Deserialize<CreateUserDto>(requestBody, 
                                new SystemTextJson.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            
                            email = email ?? createUserDto?.Email;
                            displayName = displayName ?? createUserDto?.DisplayName;
                        }
                        catch (SystemTextJson.JsonException ex)
                        {
                            _logger.LogWarning(ex, "Invalid JSON in request body");
                        }
                    }
                }

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(displayName))
                {
                    _logger.LogWarning("Missing email or display name from JWT and request body");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Email and display name required" });
                    return badResponse;
                }

                // Check if user already exists by Microsoft ID
                var existingUser = await _userRepository.GetUserByIdAsync(microsoftUserId);
                if (existingUser != null)
                {
                    _logger.LogInformation("User already exists: {UserId}", microsoftUserId);
                    var successResponse = req.CreateResponse(HttpStatusCode.OK);
                    await successResponse.WriteAsJsonAsync(existingUser);
                    return successResponse;
                }

                // Check if email already exists (different Microsoft account)
                existingUser = await _userRepository.GetUserByEmailAsync(email);
                if (existingUser != null)
                {
                    _logger.LogWarning("User creation failed: Email {Email} already exists with different Microsoft account", email);
                    var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflictResponse.WriteAsJsonAsync(new ErrorResponse
                    {
                        Error = "A user with this email address already exists"
                    });
                    return conflictResponse;
                }

                // Create new user
                var newUser = new User
                {
                    Id = microsoftUserId,  // Use Microsoft user ID as primary key
                    Email = email.Trim().ToLowerInvariant(),
                    DisplayName = displayName.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    Friends = new List<string>()
                };

                var createdUser = await _userRepository.CreateUserAsync(newUser);
                _logger.LogInformation("User created successfully with Microsoft ID: {UserId}", createdUser.Id);

                var response = req.CreateResponse(HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdUser);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new ErrorResponse { Error = "An unexpected error occurred" });
                return errorResponse;
            }
        }

        [Function("SearchUsers")]
        [OpenApiOperation(
            operationId: "searchUsers",
            tags: new[] { "Users" },
            Summary = "Search users",
            Description = "Search for users by display name or email address."
        )]
        [OpenApiParameter(
            name: "q",
            In = ParameterLocation.Query,
            Required = true,
            Type = typeof(string),
            Summary = "Search query",
            Description = "Search term for display name or email"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(List<User>),
            Summary = "Search results",
            Description = "Returns list of users matching the search term"
        )]
        public async Task<HttpResponseData> SearchUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/search")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Searching for users");

                // Validate JWT authentication
                var requestingUserId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(requestingUserId))
                {
                    _logger.LogWarning("Unauthorized: Invalid or missing JWT token");
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                // Get search query
                var query = req.Url.Query;
                var queryParams = System.Web.HttpUtility.ParseQueryString(query);
                var searchTerm = queryParams["q"];

                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    _logger.LogWarning("Empty search term");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Search query 'q' is required" });
                    return badResponse;
                }

                // Search users
                var users = await _userRepository.SearchUsersAsync(searchTerm.Trim());
                _logger.LogInformation("Found {Count} users matching search term", users.Count);

                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(users);
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching users");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new ErrorResponse { Error = "An unexpected error occurred" });
                return errorResponse;
            }
        }

        [Function("GetUserProfile")]
        [OpenApiOperation(
            operationId: "getUserProfile",
            tags: new[] { "Users" },
            Summary = "Get user profile",
            Description = "Retrieves a user's profile information by user ID."
        )]
        [OpenApiParameter(
            name: "userId",
            In = ParameterLocation.Path,
            Required = true,
            Type = typeof(string),
            Summary = "User ID",
            Description = "The unique identifier of the user"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(User),
            Summary = "User profile retrieved",
            Description = "Returns the user profile information"
        )]
        public async Task<HttpResponseData> GetUserProfile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{userId}/profile")] HttpRequestData req,
            string userId)
        {
            try
            {
                _logger.LogInformation("Getting user profile for ID: {UserId}", userId);

                // Validate userId parameter
                if (string.IsNullOrWhiteSpace(userId))
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "User ID is required" });
                    return badResponse;
                }

                // Validate JWT authentication
                var requestingUserId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(requestingUserId))
                {
                    _logger.LogWarning("Unauthorized: Invalid or missing JWT token");
                    var unauthorizedResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                // Get user profile
                var user = await _userRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new ErrorResponse { Error = "User not found" });
                    return notFoundResponse;
                }

                _logger.LogInformation("User profile retrieved successfully for: {UserId}", userId);
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(user);
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user profile for ID: {UserId}", userId);
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new ErrorResponse { Error = "An unexpected error occurred" });
                return errorResponse;
            }
        }
    }

    // Response models
    public class ErrorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        [Newtonsoft.Json.JsonProperty("error")]
        public string Error { get; set; } = string.Empty;
    }

    public class CreateUserDto
    {
        [JsonPropertyName("email")]
        [JsonProperty("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }
}