using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using LookatDeezBackend.Extensions;
using System.Net;
using System.Text.Json;

namespace LookatDeezBackend.Middleware
{
    public class JwtAuthenticationMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<JwtAuthenticationMiddleware> _logger;

        public JwtAuthenticationMiddleware(ILogger<JwtAuthenticationMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var request = await context.GetHttpRequestDataAsync();
            if (request != null)
            {
                // Validate JWT token
                var principal = await AuthHelper.ValidateTokenAsync(request, _logger);
                if (principal == null)
                {
                    _logger.LogWarning("Unauthorized request to {FunctionName}", context.FunctionDefinition.Name);
                    
                    // Create 401 response
                    var response = request.CreateResponse(HttpStatusCode.Unauthorized);
                    await response.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                    
                    context.GetInvocationResult().Value = response;
                    return;
                }

                // Extract user ID and add to context for functions to use
                var userId = AuthHelper.GetUserIdFromPrincipal(principal, _logger);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("No user ID found in JWT token for {FunctionName}", context.FunctionDefinition.Name);
                    
                    var response = request.CreateResponse(HttpStatusCode.Unauthorized);
                    await response.WriteAsJsonAsync(new { error = "Invalid JWT token - no user ID" });
                    
                    context.GetInvocationResult().Value = response;
                    return;
                }

                // Store authenticated user info in context
                context.Items["UserId"] = userId;
                context.Items["UserPrincipal"] = principal;
                
                _logger.LogInformation("Authenticated user {UserId} for {FunctionName}", userId, context.FunctionDefinition.Name);
            }

            // Proceed to the function
            await next(context);
        }
    }
}
