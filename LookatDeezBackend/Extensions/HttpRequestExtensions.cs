using Microsoft.Azure.Functions.Worker.Http;

namespace LookatDeezBackend.Extensions
{
    public static class HttpRequestExtensions
    {
        public static string? GetUserId(this HttpRequestData req)
        {
            // Try to get user ID from custom header first
            if (req.Headers.TryGetValues("X-User-Id", out var headerValues))
            {
                return headerValues.FirstOrDefault();
            }

            // Fallback for requests without the header (like direct API testing)
            return "test-user-123";
        }
    }
}