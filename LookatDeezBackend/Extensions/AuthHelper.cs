using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace LookatDeezBackend.Extensions
{
    public static class AuthHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static JsonWebKeySet? _cachedJwks;
        private static DateTime _jwksExpiry = DateTime.MinValue;
        
        // Your B2C CIAM tenant details
        private static string TenantId => Environment.GetEnvironmentVariable("AzureAd_TenantId") 
            ?? throw new InvalidOperationException("AzureAd_TenantId not configured");
        
        private static string ClientId => Environment.GetEnvironmentVariable("AzureAd_ClientId") 
            ?? throw new InvalidOperationException("AzureAd_ClientId not configured");

        private static string JwksUri => $"https://lookatdeez.ciamlogin.com/{TenantId}/discovery/v2.0/keys";

        public static async Task<ClaimsPrincipal?> ValidateTokenAsync(HttpRequestData req, ILogger? logger = null)
        {
            try
            {
                // Extract Bearer token
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

        public static async Task<string?> GetUserIdAsync(HttpRequestData req, ILogger? logger = null)
        {
            var principal = await ValidateTokenAsync(req, logger);
            return GetUserIdFromPrincipal(principal, logger);
        }

        public static string? GetUserIdFromPrincipal(ClaimsPrincipal? principal, ILogger? logger = null)
        {
            if (principal == null) return null;

            // Try different claim types for user ID
            var userIdClaim = principal.Claims.FirstOrDefault(c => 
                c.Type == "oid" ||      // Object ID (Microsoft preferred)
                c.Type == "sub" ||      // Subject
                c.Type == ClaimTypes.NameIdentifier);

            var userId = userIdClaim?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                logger?.LogInformation("Extracted user ID from validated token: {UserId}", userId);
                return userId;
            }

            logger?.LogWarning("No user ID claim found in validated token");
            return null;
        }

        private static async Task<ClaimsPrincipal?> ValidateJwtTokenAsync(string token, ILogger? logger = null)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                
                if (!handler.CanReadToken(token))
                {
                    logger?.LogWarning("Invalid JWT token format");
                    return null;
                }

                // Get JWKS for token validation
                var jwks = await GetJwksAsync(logger);
                if (jwks == null)
                {
                    logger?.LogError("Failed to retrieve JWKS");
                    return null;
                }

                // Read token to get key ID
                var jsonToken = handler.ReadJwtToken(token);
                var kid = jsonToken.Header.Kid;

                if (string.IsNullOrEmpty(kid))
                {
                    logger?.LogWarning("JWT token missing key ID (kid)");
                    return null;
                }

                // Find matching key
                var key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);
                if (key == null)
                {
                    logger?.LogWarning("No matching key found for kid: {Kid}", kid);
                    return null;
                }

                // Validate token
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = new[] 
                    {
                        $"https://lookatdeez.ciamlogin.com/{TenantId}/v2.0",
                        $"https://lookatdeez.ciamlogin.com/{TenantId}/"
                    },
                    
                    ValidateAudience = true,
                    ValidAudiences = new[] { ClientId, "api://" + ClientId },
                    
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5),
                    
                    RequireExpirationTime = true,
                    RequireSignedTokens = true
                };

                var result = await handler.ValidateTokenAsync(token, validationParameters);
                
                if (result.IsValid)
                {
                    logger?.LogInformation("JWT token validated successfully");
                    return new ClaimsPrincipal(result.ClaimsIdentity);
                }
                else
                {
                    logger?.LogWarning("JWT token validation failed: {Exception}", result.Exception?.Message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Exception during JWT token validation");
                return null;
            }
        }

        private static async Task<JsonWebKeySet?> GetJwksAsync(ILogger? logger = null)
        {
            try
            {
                // Use cached JWKS if still valid
                if (_cachedJwks != null && DateTime.UtcNow < _jwksExpiry)
                {
                    return _cachedJwks;
                }

                logger?.LogInformation("Fetching JWKS from {JwksUri}", JwksUri);
                
                var response = await _httpClient.GetAsync(JwksUri);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var jwks = new JsonWebKeySet(json);
                
                // Cache for 1 hour
                _cachedJwks = jwks;
                _jwksExpiry = DateTime.UtcNow.AddHours(1);
                
                logger?.LogInformation("JWKS cached successfully with {KeyCount} keys", jwks.Keys.Count);
                return jwks;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to fetch JWKS from {JwksUri}", JwksUri);
                return null;
            }
        }
    }
}