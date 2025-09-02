using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using System.Net;

namespace LookatDeezBackend.Functions
{
    public class SwaggerFunctions
    {
        private readonly ILogger<SwaggerFunctions> _logger;

        public SwaggerFunctions(ILogger<SwaggerFunctions> logger)
        {
            _logger = logger;
        }

        [Function("RenderSwaggerDocument")]
        [OpenApiIgnore]
        public async Task<HttpResponseData> RenderSwaggerDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger.json")] HttpRequestData req)
        {
            _logger.LogInformation("Swagger document requested");
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            
            // The OpenAPI extension should generate this automatically
            // If not, you may need to configure it in Program.cs
            return response;
        }

        [Function("RenderSwaggerUI")]
        [OpenApiIgnore]
        public async Task<HttpResponseData> RenderSwaggerUI(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger/ui")] HttpRequestData req)
        {
            _logger.LogInformation("Swagger UI requested");
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/html");
            
            // The OpenAPI extension should serve the UI automatically
            // If not, you may need to configure it in Program.cs
            return response;
        }

        [Function("RenderOpenApiDocument")]
        [OpenApiIgnore]
        public async Task<HttpResponseData> RenderOpenApiDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi/v3.json")] HttpRequestData req)
        {
            _logger.LogInformation("OpenAPI v3 document requested");
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            
            // The OpenAPI extension should generate this automatically
            return response;
        }
    }
}
