using LookatDeezBackend.Data.Models;
using LookatDeezBackend.Data.Repositories;
using LookatDeezBackend.Helpers;
using LookatDeezBackend.Settings;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using SystemTextJson = System.Text.Json;

namespace LookatDeezBackend.Functions
{
    public class AuthFunctions
    {
        private readonly IUserRepository _userRepository;
        private readonly JwtSettings _jwtSettings;
        private readonly ILogger<AuthFunctions> _logger;

        public AuthFunctions(IUserRepository userRepository, JwtSettings jwtSettings, ILogger<AuthFunctions> logger)
        {
            _userRepository = userRepository;
            _jwtSettings = jwtSettings;
            _logger = logger;
        }

        [Function("Register")]
        public async Task<HttpResponseData> Register(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "auth/register")] HttpRequestData req)
        {
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                return await CorsHelper.HandlePreflightRequest(req);

            try
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                var request = SystemTextJson.JsonSerializer.Deserialize<RegisterRequest>(body,
                    new SystemTextJson.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.DisplayName))
                {
                    var badResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Email, password, and display name are required" });
                    return badResponse;
                }

                var email = request.Email.Trim().ToLowerInvariant();

                if (request.Password.Length < 8)
                {
                    var badResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Password must be at least 8 characters" });
                    return badResponse;
                }

                var existing = await _userRepository.GetUserByEmailAsync(email);
                if (existing != null)
                {
                    var conflictResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Conflict);
                    await conflictResponse.WriteAsJsonAsync(new { error = "An account with this email already exists" });
                    return conflictResponse;
                }

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                var newUser = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Email = email,
                    DisplayName = request.DisplayName.Trim(),
                    PasswordHash = passwordHash,
                    AuthProvider = "local",
                    CreatedAt = DateTime.UtcNow,
                    Friends = new List<string>()
                };

                var createdUser = await _userRepository.CreateUserAsync(newUser);
                _logger.LogInformation("Local user registered: {Email}", email);

                var token = GenerateJwt(createdUser);

                var response = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Created);
                await response.WriteAsJsonAsync(new AuthResponse
                {
                    Token = token.Token,
                    ExpiresAt = token.ExpiresAt,
                    User = new AuthUserInfo
                    {
                        Id = createdUser.Id,
                        Email = createdUser.Email,
                        DisplayName = createdUser.DisplayName
                    }
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
                return errorResponse;
            }
        }

        [Function("Login")]
        public async Task<HttpResponseData> Login(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "auth/login")] HttpRequestData req)
        {
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                return await CorsHelper.HandlePreflightRequest(req);

            try
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                var request = SystemTextJson.JsonSerializer.Deserialize<LoginRequest>(body,
                    new SystemTextJson.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                {
                    var badResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Email and password are required" });
                    return badResponse;
                }

                var email = request.Email.Trim().ToLowerInvariant();
                var user = await _userRepository.GetUserByEmailAsync(email);

                if (user == null || user.AuthProvider != "local" || string.IsNullOrEmpty(user.PasswordHash))
                {
                    var unauthorizedResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Invalid email or password" });
                    return unauthorizedResponse;
                }

                if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    var unauthorizedResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.Unauthorized);
                    await unauthorizedResponse.WriteAsJsonAsync(new { error = "Invalid email or password" });
                    return unauthorizedResponse;
                }

                _logger.LogInformation("Local user logged in: {Email}", email);

                var token = GenerateJwt(user);

                var response = CorsHelper.CreateCorsResponse(req, HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new AuthResponse
                {
                    Token = token.Token,
                    ExpiresAt = token.ExpiresAt,
                    User = new AuthUserInfo
                    {
                        Id = user.Id,
                        Email = user.Email,
                        DisplayName = user.DisplayName
                    }
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
                return errorResponse;
            }
        }

        private TokenResult GenerateJwt(User user)
        {
            var expires = DateTime.UtcNow.AddHours(24);

            var claims = new[]
            {
                new Claim("oid", user.Id),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
                new Claim("auth_provider", "local"),
            };

            var token = new JwtSecurityToken(
                issuer: JwtSettings.Issuer,
                audience: JwtSettings.Audience,
                claims: claims,
                expires: expires,
                signingCredentials: _jwtSettings.SigningCredentials
            );

            return new TokenResult
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAt = expires.ToString("o")
            };
        }

        private class RegisterRequest
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public string DisplayName { get; set; } = "";
        }

        private class LoginRequest
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
        }

        private class TokenResult
        {
            public string Token { get; set; } = "";
            public string ExpiresAt { get; set; } = "";
        }

        private class AuthResponse
        {
            public string Token { get; set; } = "";
            public string ExpiresAt { get; set; } = "";
            public AuthUserInfo User { get; set; } = new();
        }

        private class AuthUserInfo
        {
            public string Id { get; set; } = "";
            public string Email { get; set; } = "";
            public string DisplayName { get; set; } = "";
        }
    }
}
