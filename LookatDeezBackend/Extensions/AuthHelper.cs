using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace LookatDeezBackend.Extensions
{
    public static class AuthHelper
    {
        public static string? GetUserId(HttpRequestData req, ILogger? logger = null)
        {
            try
            {
                // Try to get user ID from JWT token first
                if (req.Headers.TryGetValues("Authorization", out var authHeaders))
                {
                    var authHeader = authHeaders.FirstOrDefault();
                    if (authHeader?.StartsWith("Bearer ") == true)
                    {
                        var token = authHeader.Substring(7);
                        var userId = ExtractUserIdFromJwt(token, logger);
                        if (!string.IsNullOrEmpty(userId))
                        {
                            return userId;
                        }
                    }
                }

                // Fallback to x-user-id header
                if (req.Headers.TryGetValues("x-user-id", out var userIdHeaders))
                {
                    var userId = userIdHeaders.FirstOrDefault();
                    if (!string.IsNullOrEmpty(userId))
                    {
                        return userId;
                    }
                }

                logger?.LogWarning("No user ID found in Authorization header or x-user-id header");
                return null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error extracting user ID from request");
                return null;
            }
        }

        private static string? ExtractUserIdFromJwt(string token, ILogger? logger = null)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                
                if (handler.CanReadToken(token))
                {
                    var jsonToken = handler.ReadJwtToken(token);
                    
                    var userIdClaim = jsonToken.Claims.FirstOrDefault(c => 
                        c.Type == "oid" ||      // Object ID (preferred for Microsoft)
                        c.Type == "sub" ||      // Subject
                        c.Type == "id" ||       // Sometimes used
                        c.Type == ClaimTypes.NameIdentifier);

                    if (userIdClaim != null)
                    {
                        logger?.LogInformation("Extracted user ID from JWT: {UserId}", userIdClaim.Value);
                        return userIdClaim.Value;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error parsing JWT token");
                return null;
            }
        }
    }
}