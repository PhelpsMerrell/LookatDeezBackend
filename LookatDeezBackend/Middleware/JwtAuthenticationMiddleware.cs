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
            "DebugToken",
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
                // Skip middleware for OPTIONS requests (CORS preflight)
                if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Skipping JWT validation for OPTIONS request");
                    await next(context);
                    return;
                }

                // Check if this function is public
                var functionName = context.FunctionDefinition.Name;
                var isPublicFunction = PublicFunctions.Contains(functionName);
                
                // Validate JWT token
                var principal = await AuthHelper.ValidateTokenAsync(request, _logger);
                if (principal == null)
                {
                    if (!isPublicFunction)
                    {
                        _logger.LogWarning("Unauthorized request to protected function {FunctionName}", functionName);
                        
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
                    // Extract user ID and add to context for functions to use
                    var userId = AuthHelper.GetUserIdFromPrincipal(principal, _logger);
                    if (string.IsNullOrEmpty(userId))
                    {
                        if (!isPublicFunction)
                        {
                            _logger.LogWarning("No user ID found in JWT token for protected function {FunctionName}", functionName);
                            
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
                        
                        _logger.LogInformation("Authenticated user {UserId} for {FunctionName}", userId, functionName);
                    }
                }
            }

            // Proceed to the function
            await next(context);
        }
    }
}
