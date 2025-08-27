using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace LookatDeezBackend.Extensions
{
    public class OpenApiConfigurationOptions : DefaultOpenApiConfigurationOptions
    {
        public override OpenApiInfo Info { get; set; } = new OpenApiInfo()
        {
            Version = "1.0.0",
            Title = "LookAtDeez API",
            Description = "API for managing video playlists and user accounts",
            Contact = new OpenApiContact()
            {
                Name = "LookAtDeez Support",
                Email = "support@lookatdeez.com"
            }
        };

        public override List<OpenApiServer> Servers { get; set; } = new List<OpenApiServer>()
        {
            new OpenApiServer() { Url = "https://lookatdeez-functions.azurewebsites.net", Description = "Production" },
            new OpenApiServer() { Url = "http://localhost:7071", Description = "Local Development" }
        };

        public override OpenApiVersionType OpenApiVersion { get; set; } = OpenApiVersionType.V3;

      

        
    }
}
