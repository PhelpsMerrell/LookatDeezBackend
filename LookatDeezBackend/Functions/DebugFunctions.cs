using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using LookatDeezBackend.Extensions;
using LookatDeezBackend.Helpers;
using System.Net;

namespace LookatDeezBackend.Functions
{
    public class DebugFunctions
    {
        private readonly ILogger<DebugFunctions> _logger;

        public DebugFunctions(ILogger<DebugFunctions> logger)
        {
            _logger = logger;
        }

        [Function("DebugAuth")]
        public async Task<HttpResponseData> DebugAuth(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "debug/auth")] HttpRequestData req)
        {
            // Handle CORS preflight requests
            if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return await CorsHelper.HandlePreflightRequest(req);
            }

            try
            {
                var response = CorsHelper.CreateCorsResponse(req, HttpStatusCode.OK);

                _logger.LogInformation("=== DEBUG AUTH ENDPOINT ===");
                
                // Check headers
                if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
                {
                    await response.WriteAsJsonAsync(new { 
                        error = "No Authorization header found",
                        headers = req.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray())
                    });
                    return response;
                }

                var authHeader = authHeaders.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    await response.WriteAsJsonAsync(new { 
                        error = "Invalid Authorization header format",
                        authHeader = authHeader
                    });
                    return response;
                }

                var token = authHeader.Substring(7);
                _logger.LogInformation("Token length: {Length}", token.Length);
                
                // Validate the token
                var principal = await AuthHelper.ValidateTokenAsync(req, _logger);
                
                if (principal == null)
                {
                    await response.WriteAsJsonAsync(new { 
                        error = "JWT validation failed",
                        tokenPreview = token.Substring(0, Math.Min(50, token.Length)) + "..."
                    });
                    return response;
                }

                var userId = AuthHelper.GetUserIdFromPrincipal(principal, _logger);
                var claims = principal.Claims.ToDictionary(c => c.Type, c => c.Value);

                await response.WriteAsJsonAsync(new { 
                    success = true,
                    userId = userId,
                    claimsCount = claims.Count,
                    claims = claims,
                    message = "JWT validation successful"
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in debug auth endpoint");
                var errorResponse = CorsHelper.CreateCorsResponse(req, HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { 
                    error = "Exception occurred", 
                    message = ex.Message,
                    stackTrace = ex.StackTrace 
                });
                return errorResponse;
            }
        }
    }
}
