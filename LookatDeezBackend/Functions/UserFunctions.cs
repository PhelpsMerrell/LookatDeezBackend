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
using LookatDeezBackend.Helpers;
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
            operationId: "CreateUser",
            tags: new[] { "Users" },
            Summary = "Create or verify user account",
            Description = "Creates a new user account or returns existing user if already exists. This endpoint is called automatically after successful Microsoft authentication."
        )]
        [OpenApiRequestBody(
            contentType: "application/json",
            bodyType: typeof(CreateUserDto),
            Required = false,
            Description = "Optional user details. If not provided, defaults will be used from JWT token."
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Created,
            contentType: "application/json",
            bodyType: typeof(User),
            Description = "User created successfully."
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(User),
            Description = "User already exists and was returned."
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.Conflict,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Description = "A user with this email address already exists."
        )]
        [OpenApiResponseWithoutBody(
            statusCode: HttpStatusCode.Unauthorized,
            Description = "User not authenticated - valid JWT token required."
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.InternalServerError,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Description = "An unexpected error occurred."
        )]
        public async Task<HttpResponseData> CreateUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "users")] HttpRequestData req,
            FunctionContext context)
        {
            // Handle CORS preflight requests
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return await CorsHelper.HandlePreflightRequest(req);
            }

            try
            {
                var microsoftUserId = context.GetUserId();

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                string email = "unknown@example.com";
                string displayName = "User";
                
                if (!string.IsNullOrWhiteSpace(requestBody))
                {
                    try
                    {
                        var createUserDto = SystemTextJson.JsonSerializer.Deserialize<CreateUserDto>(requestBody, 
                            new SystemTextJson.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        if (!string.IsNullOrEmpty(createUserDto?.Email))
                            email = createUserDto.Email;
                        if (!string.IsNullOrEmpty(createUserDto?.DisplayName))
                            displayName = createUserDto.DisplayName;
                    }
                    catch (SystemTextJson.JsonException ex)
                    {
                        _logger.LogWarning(ex, "Invalid JSON in request body, using defaults");
                    }
                }

                var existingUser = await _userRepository.GetUserByIdAsync(microsoftUserId);
                if (existingUser != null)
                {
                    var successResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.OK);
                    await successResponse.WriteAsJsonAsync(existingUser);
                    return successResponse;
                }

                existingUser = await _userRepository.GetUserByEmailAsync(email);
                if (existingUser != null)
                {
                    var conflictResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Conflict);
                    await conflictResponse.WriteAsJsonAsync(new ErrorResponse
                    {
                        Error = "A user with this email address already exists"
                    });
                    return conflictResponse;
                }

                var newUser = new User
                {
                    Id = microsoftUserId,
                    Email = email.Trim().ToLowerInvariant(),
                    DisplayName = displayName.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    Friends = new List<string>()
                };

                var createdUser = await _userRepository.CreateUserAsync(newUser);
                var response = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Created);
                await response.WriteAsJsonAsync(createdUser);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
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
        [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(List<User>),
            Summary = "Search results",
            Description = "Returns list of users matching the search term"
        )]
        public async Task<HttpResponseData> SearchUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "users/search")] HttpRequestData req)
        {
            // Handle CORS preflight requests
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return await CorsHelper.HandlePreflightRequest(req);
            }

            try
            {
                _logger.LogInformation("Searching for users");

                // Validate JWT authentication
                var requestingUserId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(requestingUserId))
                {
                    _logger.LogWarning("Unauthorized: Invalid or missing JWT token");
                    var unauthorizedResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Unauthorized);
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
                    var badResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Search query 'q' is required" });
                    return badResponse;
                }

                // Search users
                var users = await _userRepository.SearchUsersAsync(searchTerm.Trim());
                _logger.LogInformation("Found {Count} users matching search term", users.Count);

                var successResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(users);
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching users");
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
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
        [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(User),
            Summary = "User profile retrieved",
            Description = "Returns the user profile information"
        )]
        public async Task<HttpResponseData> GetUserProfile(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "users/{userId}/profile")] HttpRequestData req,
            string userId)
        {
            // Handle CORS preflight requests
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return await CorsHelper.HandlePreflightRequest(req);
            }

            try
            {
                _logger.LogInformation("Getting user profile for ID: {UserId}", userId);

                // Validate userId parameter
                if (string.IsNullOrWhiteSpace(userId))
                {
                    var badResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "User ID is required" });
                    return badResponse;
                }

                // Validate JWT authentication
                var requestingUserId = await AuthHelper.GetUserIdAsync(req, _logger);
                if (string.IsNullOrEmpty(requestingUserId))
                {
                    _logger.LogWarning("Unauthorized: Invalid or missing JWT token");
                    var unauthorizedResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Valid JWT token required" });
                    return unauthorizedResponse;
                }

                // Get user profile
                var user = await _userRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    var notFoundResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new ErrorResponse { Error = "User not found" });
                    return notFoundResponse;
                }

                _logger.LogInformation("User profile retrieved successfully for: {UserId}", userId);
                var successResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(user);
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user profile for ID: {UserId}", userId);
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
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