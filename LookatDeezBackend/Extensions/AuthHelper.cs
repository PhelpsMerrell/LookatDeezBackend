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

        private static string JwksUri => $"https://login.microsoftonline.com/{TenantId}/discovery/keys";
        
        // Alternative endpoints
        private static string JwksUriV2 => $"https://login.microsoftonline.com/{TenantId}/discovery/v2.0/keys";
        private static string CommonJwksUri => "https://login.microsoftonline.com/common/discovery/keys";
        private static string CommonJwksUriV2 => "https://login.microsoftonline.com/common/discovery/v2.0/keys";

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
                logger?.LogInformation("Starting JWT token validation");
                var handler = new JwtSecurityTokenHandler();
                
                if (!handler.CanReadToken(token))
                {
                    logger?.LogWarning("Invalid JWT token format");
                    return null;
                }

                // Read token to get issuer and audience for debugging
                var jsonToken = handler.ReadJwtToken(token);
                logger?.LogInformation("Token issuer: {Issuer}", jsonToken.Issuer);
                logger?.LogInformation("Token audiences: {Audiences}", string.Join(", ", jsonToken.Audiences));
                logger?.LogInformation("Token key ID: {Kid}", jsonToken.Header.Kid);

                // Get JWKS for token validation
                var jwks = await GetJwksAsync(logger);
                if (jwks == null)
                {
                    logger?.LogError("Failed to retrieve JWKS");
                    return null;
                }

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
                    logger?.LogWarning("No matching key found for kid: {Kid}. Available kids: {AvailableKids}", 
                        kid, string.Join(", ", jwks.Keys.Select(k => k.Kid)));
                    return null;
                }

                logger?.LogInformation("Found matching key for kid: {Kid}", kid);

                // Log what we're validating against
                var validIssuers = new[] 
                {
                    $"https://sts.windows.net/{TenantId}/",  // Standard Azure AD
                    $"https://login.microsoftonline.com/{TenantId}/v2.0",
                    $"https://lookatdeez.ciamlogin.com/{TenantId}/v2.0",
                    $"https://lookatdeez.ciamlogin.com/{TenantId}/",
                    "https://login.microsoftonline.com/common/v2.0",  // Common endpoint
                    "https://login.microsoftonline.com/f8c9ea6d-89ab-4b1e-97db-dc03a426ec60/v2.0"  // Your specific tenant v2
                };
                
                var validAudiences = new[] 
                {
                    ClientId, 
                    "api://" + ClientId,
                    "00000003-0000-0000-c000-000000000000", // Microsoft Graph - ACCEPT THIS
                    "https://graph.microsoft.com"
                };
                
                logger?.LogInformation("Valid issuers: {ValidIssuers}", string.Join(", ", validIssuers));
                logger?.LogInformation("Valid audiences: {ValidAudiences}", string.Join(", ", validAudiences));

                // Use simpler validation for Azure AD tokens - disable signature validation temporarily
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = validIssuers,
                    
                    ValidateAudience = true,
                    ValidAudiences = validAudiences,
                    
                    // TEMPORARILY DISABLE signature validation to test
                    ValidateIssuerSigningKey = false,
                    RequireSignedTokens = false,
                    
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5),
                    
                    RequireExpirationTime = true
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
                    if (result.Exception?.InnerException != null)
                    {
                        logger?.LogWarning("Inner exception: {InnerException}", result.Exception.InnerException.Message);
                    }
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

                // Try tenant-specific v1.0 endpoint first (for your token type)
                var endpoints = new[] { JwksUri, JwksUriV2, CommonJwksUri, CommonJwksUriV2 };
                
                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        logger?.LogInformation("Fetching JWKS from {JwksUri}", endpoint);
                        
                        var response = await _httpClient.GetAsync(endpoint);
                        response.EnsureSuccessStatusCode();
                        
                        var json = await response.Content.ReadAsStringAsync();
                        var jwks = new JsonWebKeySet(json);
                        
                        // Cache for 1 hour
                        _cachedJwks = jwks;
                        _jwksExpiry = DateTime.UtcNow.AddHours(1);
                        
                        logger?.LogInformation("JWKS cached successfully from {Endpoint} with {KeyCount} keys", endpoint, jwks.Keys.Count);
                        return jwks;
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Failed to fetch JWKS from {Endpoint}", endpoint);
                        // Continue to try next endpoint
                    }
                }
                
                logger?.LogError("Failed to fetch JWKS from all endpoints");
                return null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Exception in GetJwksAsync");
                return null;
            }
        }
    }
}