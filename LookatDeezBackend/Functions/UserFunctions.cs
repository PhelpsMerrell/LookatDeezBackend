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
// Explicitly use System.Text.Json to avoid ambiguity
using SystemTextJson = System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

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
            Description = "Creates a new user account with email and display name validation."
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
            statusCode: HttpStatusCode.Conflict,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Email already exists",
            Description = "A user with this email address already exists"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.InternalServerError,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Internal server error",
            Description = "An unexpected error occurred"
        )]
        public async Task<HttpResponseData> CreateUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Creating new user");

                // Read and validate request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    _logger.LogWarning("Empty request body received");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Request body is required" });
                    return badResponse;
                }

                CreateUserDto? createUserDto;
                try
                {
                    // Use explicit System.Text.Json to avoid ambiguity
                    createUserDto = SystemTextJson.JsonSerializer.Deserialize<CreateUserDto>(requestBody, new SystemTextJson.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (SystemTextJson.JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON in request body");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Invalid JSON format" });
                    return badResponse;
                }

                if (createUserDto == null)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Invalid request data" });
                    return badResponse;
                }

                // Validate input
                var validationResult = ValidateCreateUserDto(createUserDto);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Validation failed: {Errors}", string.Join(", ", validationResult.Errors));
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ValidationErrorResponse
                    {
                        Error = "Validation failed",
                        Details = validationResult.Errors
                    });
                    return badResponse;
                }

                // Check if email already exists
                var existingUser = await _userRepository.GetUserByEmailAsync(createUserDto.Email);
                if (existingUser != null)
                {
                    _logger.LogWarning("User creation failed: Email {Email} already exists", createUserDto.Email);
                    var conflictResponse = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflictResponse.WriteAsJsonAsync(new ErrorResponse
                    {
                        Error = "A user with this email address already exists"
                    });
                    return conflictResponse;
                }

                // Check if user is creating via Microsoft auth
                var userIdFromToken = AuthHelper.GetUserId(req, _logger);
                
                // Create new user
                var newUser = new User
                {
                    Id = userIdFromToken ?? Guid.NewGuid().ToString(),
                    Email = createUserDto.Email.Trim().ToLowerInvariant(),
                    DisplayName = createUserDto.DisplayName.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    Friends = new List<string>()
                };

                var createdUser = await _userRepository.CreateUserAsync(newUser);

                _logger.LogInformation("User created successfully with ID: {UserId}", createdUser.Id);

                var successResponse = req.CreateResponse(HttpStatusCode.Created);
                await successResponse.WriteAsJsonAsync(createdUser);
                return successResponse;
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
        [OpenApiParameter(
            name: "x-user-id",
            In = ParameterLocation.Header,
            Required = true,
            Type = typeof(string),
            Summary = "Requesting User ID",
            Description = "The ID of the user making the request"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(List<User>),
            Summary = "Search results",
            Description = "Returns list of users matching the search term"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.BadRequest,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Invalid request",
            Description = "Missing search query or x-user-id header"
        )]
        public async Task<HttpResponseData> SearchUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/search")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Searching for users");

                // Validate authentication
                var requestingUserId = AuthHelper.GetUserId(req, _logger);
                if (string.IsNullOrEmpty(requestingUserId))
                {
                    _logger.LogWarning("No valid authentication found");
                    var badResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Authentication required" });
                    return badResponse;
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

                _logger.LogInformation("Searching for users with term: {SearchTerm}", searchTerm);

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
        [OpenApiParameter(
            name: "x-user-id",
            In = ParameterLocation.Header,
            Required = true,
            Type = typeof(string),
            Summary = "Requesting User ID",
            Description = "The ID of the user making the request"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.OK,
            contentType: "application/json",
            bodyType: typeof(User),
            Summary = "User profile retrieved",
            Description = "Returns the user profile information"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.BadRequest,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Invalid request",
            Description = "The user ID is invalid or missing x-user-id header"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.NotFound,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "User not found",
            Description = "No user exists with the specified ID"
        )]
        [OpenApiResponseWithBody(
            statusCode: HttpStatusCode.InternalServerError,
            contentType: "application/json",
            bodyType: typeof(ErrorResponse),
            Summary = "Internal server error",
            Description = "An unexpected error occurred"
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
                    _logger.LogWarning("Empty userId parameter");
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "User ID is required" });
                    return badResponse;
                }

                // Validate authentication (JWT or x-user-id header)
                var requestingUserId = AuthHelper.GetUserId(req, _logger);
                if (string.IsNullOrEmpty(requestingUserId))
                {
                    _logger.LogWarning("No valid authentication found");
                    var badResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
                    await badResponse.WriteAsJsonAsync(new ErrorResponse { Error = "Authentication required" });
                    return badResponse;
                }
                _logger.LogInformation("Request from user: {RequestingUserId} for profile: {UserId}",
                    requestingUserId, userId);

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

        private static ValidationResult ValidateCreateUserDto(CreateUserDto dto)
        {
            var errors = new List<string>();

            // Validate email
            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                errors.Add("Email is required");
            }
            else
            {
                var emailAttribute = new EmailAddressAttribute();
                if (!emailAttribute.IsValid(dto.Email))
                {
                    errors.Add("Email format is invalid");
                }
                else if (dto.Email.Length > 254) // RFC 5321 limit
                {
                    errors.Add("Email is too long (maximum 254 characters)");
                }
            }

            // Validate display name
            if (string.IsNullOrWhiteSpace(dto.DisplayName))
            {
                errors.Add("Display name is required");
            }
            else
            {
                var trimmedDisplayName = dto.DisplayName.Trim();
                if (trimmedDisplayName.Length < 2)
                {
                    errors.Add("Display name must be at least 2 characters long");
                }
                else if (trimmedDisplayName.Length > 50)
                {
                    errors.Add("Display name must be no more than 50 characters long");
                }
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }
    }

    // Response models for OpenAPI documentation
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class ErrorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        [Newtonsoft.Json.JsonProperty("error")]
        public string Error { get; set; } = string.Empty;
    }

    public class ValidationErrorResponse : ErrorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("details")]
        [Newtonsoft.Json.JsonProperty("details")]
        public List<string> Details { get; set; } = new List<string>();
    }

/// <summary>
/// Data transfer object for creating a new user
/// </summary>
public class CreateUserDto
    {
        /// <summary>
        /// The user's email address (required, must be valid email format)
        /// </summary>
        /// <example>john.doe@example.com</example>
        [JsonPropertyName("email")]
        [JsonProperty("email")]
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Email format is invalid")]
        [StringLength(254, ErrorMessage = "Email is too long (maximum 254 characters)")]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The user's display name (required, 2-50 characters)
        /// </summary>
        /// <example>John Doe</example>
        [JsonPropertyName("displayName")]
        [JsonProperty("displayName")]
        [Required(ErrorMessage = "Display name is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Display name must be between 2 and 50 characters")]
        public string DisplayName { get; set; } = string.Empty;
    }
}