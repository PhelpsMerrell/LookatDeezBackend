using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using LookatDeezBackend.Extensions;
using LookatDeezBackend.Helpers;
using System.Net;
using System.Text.Json;

namespace LookatDeezBackend.Middleware
{
    public class JwtAuthenticationMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<JwtAuthenticationMiddleware> _logger;
        
        // Functions that don't require authentication
        private static readonly HashSet<string> PublicFunctions = new()
        {
            "CreateUser", // User creation is special - uses JWT but creates user if not exists
            // Add other public functions here if needed
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
                
                // Log all headers for debugging
                _logger.LogInformation("Request Headers:");
                foreach (var header in request.Headers)
                {
                    _logger.LogInformation("  {Key}: {Value}", header.Key, string.Join(", ", header.Value));
                }

                // Skip middleware for OPTIONS requests (CORS preflight)
                if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Skipping JWT validation for OPTIONS request");
                    await next(context);
                    return;
                }

                // Check if this function is public
                var isPublicFunction = PublicFunctions.Contains(functionName);
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
    }
}
