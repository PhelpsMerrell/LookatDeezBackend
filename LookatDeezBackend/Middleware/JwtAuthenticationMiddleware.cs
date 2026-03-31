using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using LookatDeezBackend.Extensions;
using LookatDeezBackend.Helpers;
using LookatDeezBackend.Settings;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Collections.Concurrent;

namespace LookatDeezBackend.Middleware
{
    public class JwtAuthenticationMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<JwtAuthenticationMiddleware> _logger;
        private readonly JwtSettings _jwtSettings;

        private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);
        private static readonly int MaxRequestsPerWindow = 60;
        private static readonly ConcurrentDictionary<string, List<DateTime>> RequestTracker = new();

        private static readonly HashSet<string> PublicFunctions = new()
        {
            "CreateUser",
            "Register",
            "Login",
            "RenderSwaggerDocument",
            "RenderSwaggerUI",
            "RenderOpenApiDocument",
            "RenderOAuth2Redirect",
        };

        private static readonly HashSet<string> RateLimitExemptFunctions = new()
        {
            "RenderSwaggerDocument",
            "RenderSwaggerUI",
            "RenderOpenApiDocument",
            "RenderOAuth2Redirect"
        };

        public JwtAuthenticationMiddleware(ILogger<JwtAuthenticationMiddleware> logger, JwtSettings jwtSettings)
        {
            _logger = logger;
            _jwtSettings = jwtSettings;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var request = await context.GetHttpRequestDataAsync();
            if (request != null)
            {
                var functionName = context.FunctionDefinition.Name;

                if (request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    await next(context);
                    return;
                }

                // Rate limiting
                var isRateLimitExempt = RateLimitExemptFunctions.Contains(functionName) ||
                                       functionName.Contains("OpenApi", StringComparison.OrdinalIgnoreCase) ||
                                       functionName.Contains("Swagger", StringComparison.OrdinalIgnoreCase);

                if (!isRateLimitExempt)
                {
                    var clientIdentifier = GetClientIdentifier(request);
                    if (!CheckRateLimit(clientIdentifier))
                    {
                        var rateLimitResponse = CorsHelper.CreateCorsResponse(request, HttpStatusCode.TooManyRequests);
                        rateLimitResponse.Headers.Add("Retry-After", "60");
                        await rateLimitResponse.WriteAsJsonAsync(new { error = "Rate limit exceeded.", retryAfter = 60 });
                        context.GetInvocationResult().Value = rateLimitResponse;
                        return;
                    }
                }

                var isPublicFunction = PublicFunctions.Contains(functionName) ||
                                       functionName.Contains("OpenApi", StringComparison.OrdinalIgnoreCase) ||
                                       functionName.Contains("Swagger", StringComparison.OrdinalIgnoreCase);

                ClaimsPrincipal? principal = null;
                string? userId = null;

                // First try local JWT (uses injected JwtSettings — no env var lookup)
                principal = ValidateLocalToken(request);
                if (principal != null)
                {
                    userId = principal.Claims.FirstOrDefault(c => c.Type == "oid" || c.Type == "sub")?.Value;
                    _logger.LogInformation("Authenticated via local JWT: {UserId}", userId);
                }

                // If local didn't work, try Microsoft CIAM
                if (principal == null)
                {
                    principal = await AuthHelper.ValidateTokenAsync(request, _logger);
                    if (principal != null)
                    {
                        userId = AuthHelper.GetUserIdFromPrincipal(principal, _logger);
                        _logger.LogInformation("Authenticated via Microsoft JWT: {UserId}", userId);
                    }
                }

                if (principal == null || string.IsNullOrEmpty(userId))
                {
                    if (!isPublicFunction)
                    {
                        var response = CorsHelper.CreateCorsResponse(request, HttpStatusCode.Unauthorized);
                        await response.WriteAsJsonAsync(new { error = "Valid JWT token required" });
                        context.GetInvocationResult().Value = response;
                        return;
                    }
                }
                else
                {
                    context.Items["UserId"] = userId;
                    context.Items["UserPrincipal"] = principal;
                }
            }

            await next(context);
        }

        private ClaimsPrincipal? ValidateLocalToken(HttpRequestData request)
        {
            try
            {
                if (!request.Headers.TryGetValues("Authorization", out var authHeaders))
                    return null;

                var authHeader = authHeaders.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                    return null;

                var token = authHeader.Substring(7);
                var handler = new JwtSecurityTokenHandler();

                if (!handler.CanReadToken(token))
                    return null;

                var jwt = handler.ReadJwtToken(token);
                if (jwt.Issuer != JwtSettings.Issuer)
                    return null;

                // Use the injected JwtSettings — key comes from Key Vault in production
                var validationParams = _jwtSettings.GetValidationParameters();
                var result = handler.ValidateToken(token, validationParams, out _);
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static string GetClientIdentifier(HttpRequestData request)
        {
            var ipHeaders = new[] { "X-Forwarded-For", "X-Real-IP", "X-Client-IP", "CF-Connecting-IP" };
            foreach (var header in ipHeaders)
            {
                if (request.Headers.TryGetValues(header, out var values) && values.Any())
                {
                    var ip = values.First().Split(',')[0].Trim();
                    if (!string.IsNullOrEmpty(ip)) return ip;
                }
            }
            return request.Headers.TryGetValues("client-ip", out var clientIp) && clientIp.Any()
                ? clientIp.First()
                : "unknown";
        }

        private bool CheckRateLimit(string clientIdentifier)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.Subtract(RateLimitWindow);
            var requestHistory = RequestTracker.GetOrAdd(clientIdentifier, _ => new List<DateTime>());

            lock (requestHistory)
            {
                requestHistory.RemoveAll(ts => ts < windowStart);
                if (requestHistory.Count >= MaxRequestsPerWindow) return false;
                requestHistory.Add(now);
                return true;
            }
        }
    }
}
