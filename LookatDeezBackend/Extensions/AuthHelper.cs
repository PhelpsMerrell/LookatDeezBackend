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
        private static string TenantId => Environment.GetEnvironmentVariable("AzureAd_TenantId") ?? "f8c9ea6d-89ab-4b1e-97db-dc03a426ec60";
        private static string BackendClientId => Environment.GetEnvironmentVariable("AzureAd_ClientId") ?? "44c46a0b-0c02-4e97-be76-cbe30edc3829"; // Backend API Client ID
        private static string FrontendClientId => "f0749993-27a7-486f-930d-16a825e017bf"; // Frontend App Client ID
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
                    logger?.LogInformation("No Authorization header found");
                    return null;
                }

                var authHeader = authHeaders.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    logger?.LogWarning("Invalid Authorization header format");
                    return null;
                }

                var token = authHeader.Substring(7);
                logger?.LogInformation("Validating JWT token...");
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

            // Log all claims for debugging
            if (logger != null && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Available claims in token:");
                foreach (var claim in principal.Claims)
                {
                    logger.LogDebug("  {Type}: {Value}", claim.Type, claim.Value);
                }
            }

            // For CIAM tokens, try different claim types in order of preference
            var userIdClaim = principal.Claims.FirstOrDefault(c =>
                c.Type == "oid" ||      // Object ID (most reliable for CIAM)
                c.Type == "sub" ||      // Subject  
                c.Type == ClaimTypes.NameIdentifier);

            var userId = userIdClaim?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                logger?.LogInformation("Extracted user ID from claim '{ClaimType}': {UserId}", userIdClaim.Type, userId);
                return userId;
            }

            logger?.LogWarning("No user ID claim found in token. Available claims: {Claims}",
                string.Join(", ", principal.Claims.Select(c => c.Type)));
            return null;
        }

        private static async Task<ClaimsPrincipal?> ValidateJwtTokenAsync(string token, ILogger? logger = null)
        {
            try
            {
                logger?.LogInformation("Starting JWT token validation for CIAM");
                var handler = new JwtSecurityTokenHandler();

                if (!handler.CanReadToken(token))
                {
                    logger?.LogWarning("Invalid JWT token format");
                    return null;
                }

                var jsonToken = handler.ReadJwtToken(token);
                logger?.LogInformation("Token details - Issuer: {Issuer}, Audience: {Audience}, Kid: {Kid}",
                    jsonToken.Issuer, string.Join(", ", jsonToken.Audiences), jsonToken.Header.Kid);

                var jwks = await GetJwksAsync(logger);
                if (jwks == null)
                {
                    logger?.LogError("Failed to retrieve JWKS keys");
                    return null;
                }

                var kid = jsonToken.Header.Kid;
                var key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);
                if (key == null)
                {
                    logger?.LogWarning("No matching key found for kid: {Kid}. Available keys: {KeyIds}",
                        kid, string.Join(", ", jwks.Keys.Select(k => k.Kid)));
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
                        $"https://login.microsoftonline.com/{TenantId}/v2.0",
                        // B2C user flow specific issuers
                        $"https://lookatdeez.b2clogin.com/{TenantId}/v2.0/",
                        $"https://lookatdeez.b2clogin.com/lookatdeez.onmicrosoft.com/{UserFlow}/v2.0/"
                    },

                    ValidateAudience = true,
                    ValidAudiences = new[]
                    {
                        FrontendClientId, // Frontend app tokens
                        BackendClientId,  // Backend API tokens
                        $"https://lookatdeez.onmicrosoft.com/{BackendClientId}/access",
                        $"https://lookatdeez.onmicrosoft.com/{FrontendClientId}/access"
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
                logger?.LogError(ex, "Exception during JWT validation");
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

                // CIAM JWKS endpoint with user flow
                var jwksUri = $"https://lookatdeez.ciamlogin.com/{TenantId}/discovery/v2.0/keys?p={UserFlow}";
                logger?.LogInformation("Fetching JWKS from: {JwksUri}", jwksUri);

                var response = await _httpClient.GetAsync(jwksUri);

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("JWKS request failed with status: {StatusCode}", response.StatusCode);
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger?.LogWarning("JWKS error response: {ErrorContent}", errorContent);

                    // Try fallback endpoint
                    var fallbackJwksUri = $"https://login.microsoftonline.com/{TenantId}/discovery/v2.0/keys";
                    logger?.LogInformation("Trying fallback JWKS endpoint: {FallbackUri}", fallbackJwksUri);

                    response = await _httpClient.GetAsync(fallbackJwksUri);
                    if (!response.IsSuccessStatusCode)
                    {
                        logger?.LogError("Fallback JWKS request also failed with status: {StatusCode}", response.StatusCode);
                        return null;
                    }
                }

                var json = await response.Content.ReadAsStringAsync();
                var jwks = new JsonWebKeySet(json);

                _cachedJwks = jwks;
                _jwksExpiry = DateTime.UtcNow.AddHours(1);

                logger?.LogInformation("JWKS loaded successfully with {KeyCount} keys", jwks.Keys.Count);
                logger?.LogDebug("Available key IDs: {KeyIds}", string.Join(", ", jwks.Keys.Select(k => k.Kid)));

                return jwks;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to retrieve JWKS");
                return null;
            }
        }
    }
}
