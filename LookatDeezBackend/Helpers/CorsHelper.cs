using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace LookatDeezBackend.Helpers
{
    public static class CorsHelper
    {
        private static readonly string[] AllowedOrigins = 
        {
            "http://localhost:5173",
            "http://localhost:49762", 
            "https://lookatdeez.com",
            "https://www.lookatdeez.com"
        };

        public static HttpResponseData CreateCorsResponse(HttpRequestData req, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var response = req.CreateResponse(statusCode);
            SetCorsHeaders(response, req);
            return response;
        }

        public static void SetCorsHeaders(HttpResponseData response, HttpRequestData req)
        {
            // Get the origin from the request
            var origin = req.Headers.FirstOrDefault(h => h.Key.Equals("origin", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
            
            // Check if the origin is allowed
            if (!string.IsNullOrEmpty(origin) && AllowedOrigins.Contains(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
            }
            else
            {
                // Fallback to primary domain for production
                response.Headers.Add("Access-Control-Allow-Origin", "https://lookatdeez.com");
            }

            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-user-id");
            response.Headers.Add("Access-Control-Allow-Credentials", "true");
            response.Headers.Add("Access-Control-Max-Age", "3600");
        }

        public static async Task<HttpResponseData> HandlePreflightRequest(HttpRequestData req)
        {
            var response = CreateCorsResponse(req, HttpStatusCode.NoContent);
            return response;
        }
    }
}
