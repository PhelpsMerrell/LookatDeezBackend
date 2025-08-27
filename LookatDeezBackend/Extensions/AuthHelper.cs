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
        
        // CIAM Configuration
        private static string TenantId => "f8c9ea6d-89ab-4b1e-97db-dc03a426ec60";
        private static string ClientId => "f0749993-27a7-486f-930d-16a825e017bf";
        private static string UserFlow => "B2C_1_signupsignin1";

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

            // For CIAM tokens, try different claim types
            var userIdClaim = principal.Claims.FirstOrDefault(c => 
                c.Type == "oid" ||      // Object ID
                c.Type == "sub" ||      // Subject  
                c.Type == "emails" ||   // B2C emails claim
                c.Type == ClaimTypes.NameIdentifier);

            var userId = userIdClaim?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                logger?.LogInformation("Extracted user ID from CIAM token: {UserId}", userId);
                return userId;
            }

            logger?.LogWarning("No user ID claim found in CIAM token");
            return null;
        }

        private static async Task<ClaimsPrincipal?> ValidateJwtTokenAsync(string token, ILogger? logger = null)
        {
            try
            {
                logger?.LogInformation("Validating CIAM JWT token");
                var handler = new JwtSecurityTokenHandler();
                
                if (!handler.CanReadToken(token))
                {
                    logger?.LogWarning("Invalid JWT token format");
                    return null;
                }

                var jsonToken = handler.ReadJwtToken(token);
                logger?.LogInformation("CIAM token issuer: {Issuer}", jsonToken.Issuer);
                logger?.LogInformation("CIAM token audience: {Audience}", string.Join(", ", jsonToken.Audiences));
                logger?.LogInformation("CIAM token kid: {Kid}", jsonToken.Header.Kid);

                var jwks = await GetJwksAsync(logger);
                if (jwks == null)
                {
                    logger?.LogError("Failed to get CIAM JWKS keys");
                    return null;
                }

                var kid = jsonToken.Header.Kid;
                var key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);
                if (key == null)
                {
                    logger?.LogWarning("No matching CIAM key found for kid: {Kid}", kid);
                    logger?.LogInformation("Available CIAM key IDs: {KeyIds}", string.Join(", ", jwks.Keys.Select(k => k.Kid)));
                    return null;
                }

                // CIAM token validation parameters
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = new[] 
                    {
                        $"https://lookatdeez.ciamlogin.com/{TenantId}/v2.0/",
                        $"https://lookatdeez.ciamlogin.com/{TenantId}/",
                        $"https://login.microsoftonline.com/{TenantId}/v2.0"  // Fallback
                    },
                    
                    ValidateAudience = true,
                    ValidAudiences = new[] 
                    {
                        ClientId,
                        $"https://lookatdeez.onmicrosoft.com/{ClientId}/access"
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
                    logger?.LogInformation("CIAM JWT token validation successful");
                    return new ClaimsPrincipal(result.ClaimsIdentity);
                }
                else
                {
                    logger?.LogWarning("CIAM JWT token validation failed: {Error}", result.Exception?.Message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "CIAM JWT validation exception");
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

                // CIAM JWKS endpoint
                var jwksUri = $"https://lookatdeez.ciamlogin.com/{TenantId}/discovery/v2.0/keys?p={UserFlow}";
                logger?.LogInformation("Fetching CIAM JWKS from: {JwksUri}", jwksUri);

                var response = await _httpClient.GetAsync(jwksUri);
                
                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("CIAM JWKS request failed with status: {StatusCode}", response.StatusCode);
                    
                    // Try fallback endpoint
                    var fallbackJwksUri = $"https://login.microsoftonline.com/{TenantId}/discovery/v2.0/keys";
                    logger?.LogInformation("Trying fallback JWKS endpoint: {FallbackUri}", fallbackJwksUri);
                    
                    response = await _httpClient.GetAsync(fallbackJwksUri);
                    response.EnsureSuccessStatusCode();
                }
                
                var json = await response.Content.ReadAsStringAsync();
                var jwks = new JsonWebKeySet(json);
                
                _cachedJwks = jwks;
                _jwksExpiry = DateTime.UtcNow.AddHours(1);
                
                logger?.LogInformation("CIAM JWKS loaded with {KeyCount} keys", jwks.Keys.Count);
                logger?.LogInformation("CIAM key IDs: {KeyIds}", string.Join(", ", jwks.Keys.Select(k => k.Kid)));
                
                return jwks;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to get CIAM JWKS");
                return null;
            }
        }
    }
}
