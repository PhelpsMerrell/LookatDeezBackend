using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using LookatDeezBackend.Extensions;
using LookatDeezBackend.Helpers;
using System.Net;
using System.Text.Json;
using System.Collections.Concurrent;

namespace LookatDeezBackend.Middleware
{
    public class JwtAuthenticationMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<JwtAuthenticationMiddleware> _logger;
        
        // Rate limiting configuration
        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);
        private static readonly int MaxRequestsPerWindow = 60; // 60 requests per minute
        private static readonly ConcurrentDictionary<string, List<DateTime>> RequestTracker = new();
        
        // Functions that don't require authentication
        private static readonly HashSet<string> PublicFunctions = new()
        {
            "CreateUser", // User creation is special - uses JWT but creates user if not exists
            "RenderSwaggerDocument", // Swagger JSON endpoint
            "RenderSwaggerUI", // Swagger UI endpoint  
            "RenderOpenApiDocument", // OpenAPI JSON endpoint
            "RenderOAuth2Redirect", // OAuth redirect for Swagger UI
            // Add other public functions here if needed
        };
        
        // Rate limit exemptions (for documentation endpoints)
        private static readonly HashSet<string> RateLimitExemptFunctions = new()
        {
            "RenderSwaggerDocument",
            "RenderSwaggerUI",
            "RenderOpenApiDocument", 
            "RenderOAuth2Redirect"
        };

        public JwtAuthenticationMiddleware(ILogger<JwtAuthenticationMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var request = await context.GetHttpRequestDataAsync();
            if (request != null)
            {
                var functionName = context.FunctionDefinition.Name;
                _logger.LogInformation("=== JWT Middleware Processing {FunctionName} ===", functionName);
                _logger.LogInformation("Request Method: {Method}", request.Method);
                _logger.LogInformation("Request URL: {Url}", request.Url);

                // Skip middleware for OPTIONS requests (CORS preflight)
                if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Skipping JWT validation for OPTIONS request");
                    await next(context);
                    return;
                }
                
                // Check rate limiting (unless exempt)
                var isRateLimitExempt = RateLimitExemptFunctions.Contains(functionName) ||
                                       functionName.Contains("OpenApi", StringComparison.OrdinalIgnoreCase) ||
                                       functionName.Contains("Swagger", StringComparison.OrdinalIgnoreCase);
                
                if (!isRateLimitExempt)
                {
                    var clientIdentifier = GetClientIdentifier(request);
                    if (!await CheckRateLimit(clientIdentifier, functionName))
                    {
                        _logger.LogWarning("Rate limit exceeded for client {ClientId} on function {FunctionName}", clientIdentifier, functionName);
                        
                        var rateLimitResponse = CorsHelper.CreateCorsResponse(request, HttpStatusCode.TooManyRequests);
                        rateLimitResponse.Headers.Add("Retry-After", "60"); // Retry after 60 seconds
                        await rateLimitResponse.WriteAsJsonAsync(new { 
                            error = "Rate limit exceeded. Maximum 60 requests per minute.",
                            retryAfter = 60
                        });
                        
                        context.GetInvocationResult().Value = rateLimitResponse;
                        return;
                    }
                }
                
                // Log all headers for debugging (only in verbose mode)
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Request Headers:");
                    foreach (var header in request.Headers)
                    {
                        _logger.LogDebug("  {Key}: {Value}", header.Key, string.Join(", ", header.Value));
                    }
                }

                // Check if this function is public
                var isPublicFunction = PublicFunctions.Contains(functionName) || 
                                     functionName.Contains("OpenApi", StringComparison.OrdinalIgnoreCase) ||
                                     functionName.Contains("Swagger", StringComparison.OrdinalIgnoreCase);
                _logger.LogInformation("Function {FunctionName} is public: {IsPublic}", functionName, isPublicFunction);
                
                // Validate JWT token
                _logger.LogInformation("Starting JWT token validation...");
                var principal = await AuthHelper.ValidateTokenAsync(request, _logger);
                
                if (principal == null)
                {
                    _logger.LogWarning("JWT validation failed for function {FunctionName}", functionName);
                    
                    if (!isPublicFunction)
                    {
                        _logger.LogWarning("Blocking request to protected function {FunctionName} - no valid JWT", functionName);
                        
                        // Create 401 response with CORS headers
                        var response = CorsHelper.CreateCorsResponse(request, HttpStatusCode.Unauthorized);
                        await response.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                        
                        context.GetInvocationResult().Value = response;
                        return;
                    }
                    else
                    {
                        _logger.LogInformation("No JWT token for public function {FunctionName}, proceeding", functionName);
                    }
                }
                else
                {
                    _logger.LogInformation("JWT validation succeeded for function {FunctionName}", functionName);
                    
                    // Extract user ID and add to context for functions to use
                    var userId = AuthHelper.GetUserIdFromPrincipal(principal, _logger);
                    _logger.LogInformation("Extracted user ID from JWT: {UserId}", userId ?? "NULL");
                    
                    if (string.IsNullOrEmpty(userId))
                    {
                        _logger.LogWarning("No user ID found in JWT token for function {FunctionName}", functionName);
                        
                        if (!isPublicFunction)
                        {
                            _logger.LogWarning("Blocking request - JWT valid but no user ID for protected function {FunctionName}", functionName);
                            
                            var response = CorsHelper.CreateCorsResponse(request, HttpStatusCode.Unauthorized);
                            await response.WriteAsJsonAsync(new { error = "Invalid JWT token - no user ID" });
                            
                            context.GetInvocationResult().Value = response;
                            return;
                        }
                    }
                    else
                    {
                        // Store authenticated user info in context
                        context.Items["UserId"] = userId;
                        context.Items["UserPrincipal"] = principal;
                        
                        _logger.LogInformation("Successfully authenticated user {UserId} for function {FunctionName}", userId, functionName);
                    }
                }
            }

            _logger.LogInformation("Proceeding to execute function {FunctionName}", context.FunctionDefinition.Name);
            
            // Proceed to the function
            await next(context);
            
            _logger.LogInformation("Function {FunctionName} execution completed", context.FunctionDefinition.Name);
        }
        
        private static string GetClientIdentifier(HttpRequestData request)
        {
            // Try to get client IP from various headers
            var ipHeaders = new[] { "X-Forwarded-For", "X-Real-IP", "X-Client-IP", "CF-Connecting-IP" };
            
            foreach (var header in ipHeaders)
            {
                if (request.Headers.TryGetValues(header, out var values) && values.Any())
                {
                    var ip = values.First().Split(',')[0].Trim();
                    if (!string.IsNullOrEmpty(ip))
                        return ip;
                }
            }
            
            // Fallback to connection remote address or a default
            return request.Headers.TryGetValues("client-ip", out var clientIp) && clientIp.Any() 
                ? clientIp.First() 
                : "unknown";
        }
        
        private async Task<bool> CheckRateLimit(string clientIdentifier, string functionName)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.Subtract(RateLimitWindow);
            
            // Get or create request history for this client
            var requestHistory = RequestTracker.GetOrAdd(clientIdentifier, _ => new List<DateTime>());
            
            lock (requestHistory)
            {
                // Remove requests outside the current window
                requestHistory.RemoveAll(timestamp => timestamp < windowStart);
                
                // Check if client has exceeded rate limit
                if (requestHistory.Count >= MaxRequestsPerWindow)
                {
                    _logger.LogWarning("Client {ClientId} has made {RequestCount} requests in the last minute (limit: {MaxRequests})", 
                        clientIdentifier, requestHistory.Count, MaxRequestsPerWindow);
                    return false;
                }
                
                // Add current request to history
                requestHistory.Add(now);
                
                _logger.LogDebug("Client {ClientId} now has {RequestCount}/{MaxRequests} requests in current window", 
                    clientIdentifier, requestHistory.Count, MaxRequestsPerWindow);
                
                return true;
            }
        }
        
        // Cleanup old entries periodically (called by a background task if needed)
        public static void CleanupRateLimitData()
        {
            var cutoff = DateTime.UtcNow.Subtract(RateLimitWindow);
            var keysToRemove = new List<string>();
            
            foreach (var kvp in RequestTracker)
            {
                lock (kvp.Value)
                {
                    kvp.Value.RemoveAll(timestamp => timestamp < cutoff);
                    
                    // Remove empty entries
                    if (kvp.Value.Count == 0)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
            }
            
            // Remove empty entries
            foreach (var key in keysToRemove)
            {
                RequestTracker.TryRemove(key, out _);
            }
        }
    }
}
