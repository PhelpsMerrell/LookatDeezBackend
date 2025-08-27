using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace LookatDeezBackend.Extensions
{
    public static class AuthHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static JsonWebKeySet? _cachedJwks;
        private static DateTime _jwksExpiry = DateTime.MinValue;
        
        // Regular Azure AD configuration
        private static string TenantId => "f8c9ea6d-89ab-4b1e-97db-dc03a426ec60";
        private static string ClientId => "f0749993-27a7-486f-930d-16a825e017bf";

        public static async Task<string?> GetUserIdAsync(HttpRequestData req, ILogger? logger = null)
        {
            var principal = await ValidateTokenAsync(req, logger);
            return GetUserIdFromPrincipal(principal, logger);
        }

        public static async Task<ClaimsPrincipal?> ValidateTokenAsync(HttpRequestData req, ILogger? logger = null)
        {
            try
            {
                if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
                {
                    logger?.LogWarning("No Authorization header found");
                    return null;
                }

                var authHeader = authHeaders.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    logger?.LogWarning("Invalid Authorization header format");
                    return null;
                }

                var token = authHeader.Substring(7);
                return await ValidateJwtTokenAsync(token, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error validating token");
                return null;
            }
        }

        public static string? GetUserIdFromPrincipal(ClaimsPrincipal? principal, ILogger? logger = null)
        {
            if (principal == null) return null;

            var userIdClaim = principal.Claims.FirstOrDefault(c => 
                c.Type == "oid" ||      // Object ID (Microsoft standard)
                c.Type == "sub" ||      // Subject
                c.Type == ClaimTypes.NameIdentifier);

            var userId = userIdClaim?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                logger?.LogInformation("Extracted user ID from token: {UserId}", userId);
                return userId;
            }

            logger?.LogWarning("No user ID claim found in token");
            return null;
        }

        private static async Task<ClaimsPrincipal?> ValidateJwtTokenAsync(string token, ILogger? logger = null)
        {
            try
            {
                logger?.LogInformation("Validating JWT token");
                var handler = new JwtSecurityTokenHandler();
                
                if (!handler.CanReadToken(token))
                {
                    logger?.LogWarning("Invalid JWT token format");
                    return null;
                }

                var jsonToken = handler.ReadJwtToken(token);
                logger?.LogInformation("Token issuer: {Issuer}", jsonToken.Issuer);
                logger?.LogInformation("Token audience: {Audience}", string.Join(", ", jsonToken.Audiences));
                logger?.LogInformation("Token kid: {Kid}", jsonToken.Header.Kid);

                var jwks = await GetJwksAsync(logger);
                if (jwks == null)
                {
                    logger?.LogError("Failed to get JWKS keys");
                    return null;
                }

                var kid = jsonToken.Header.Kid;
                var key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);
                if (key == null)
                {
                    logger?.LogWarning("No matching key found for kid: {Kid}", kid);
                    return null;
                }

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = $"https://sts.windows.net/{TenantId}/",
                    
                    ValidateAudience = true,
                    ValidAudiences = new[] 
                    {
                        ClientId,
                        "00000003-0000-0000-c000-000000000000", // Microsoft Graph
                        "https://graph.microsoft.com"
                    },
                    
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = new[] { key },
                    
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5),
                    RequireExpirationTime = true
                };

                var result = await handler.ValidateTokenAsync(token, validationParameters);
                
                if (result.IsValid)
                {
                    logger?.LogInformation("JWT token validation successful");
                    return new ClaimsPrincipal(result.ClaimsIdentity);
                }
                else
                {
                    logger?.LogWarning("JWT token validation failed: {Error}", result.Exception?.Message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "JWT validation exception");
                return null;
            }
        }

        private static async Task<JsonWebKeySet?> GetJwksAsync(ILogger? logger = null)
        {
            try
            {
                if (_cachedJwks != null && DateTime.UtcNow < _jwksExpiry)
                {
                    return _cachedJwks;
                }

                var jwksUri = $"https://login.microsoftonline.com/{TenantId}/discovery/keys";
                logger?.LogInformation("Fetching JWKS from: {JwksUri}", jwksUri);

                var response = await _httpClient.GetAsync(jwksUri);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var jwks = new JsonWebKeySet(json);
                
                _cachedJwks = jwks;
                _jwksExpiry = DateTime.UtcNow.AddHours(1);
                
                logger?.LogInformation("JWKS loaded with {KeyCount} keys", jwks.Keys.Count);
                return jwks;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to get JWKS");
                return null;
            }
        }
    }
}
