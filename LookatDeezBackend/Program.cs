using LookatDeezBackend.Data.Repositories;
using LookatDeezBackend.Data.Services;
using LookatDeezBackend.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Cosmos;
using LookatDeezBackend.Extensions;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        // Register JWT authentication middleware
        builder.UseMiddleware<JwtAuthenticationMiddleware>();
    })
    .ConfigureServices(services =>
    {
        // Application Insights (isolated)
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Register CosmosClient - Updated to match your local.settings.json
        services.AddSingleton<CosmosClient>(serviceProvider =>
        {
            var connectionString = Environment.GetEnvironmentVariable("CosmosConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("CosmosConnectionString environment variable is not set");
            }
            return new CosmosClient(connectionString);
        });

        // Your existing DI services
        services.AddScoped<ICosmosService>(serviceProvider => 
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<CosmosService>>();
            return new CosmosService(configuration, logger);
        });
        services.AddScoped<AuthorizationService>();

        // Fixed UserRepository registration with proper constructor parameters
        services.AddScoped<IUserRepository>(serviceProvider =>
        {
            var cosmosClient = serviceProvider.GetRequiredService<CosmosClient>();
            var databaseName = Environment.GetEnvironmentVariable("CosmosDb_DatabaseName") ?? "lookatdeez-db";
            var logger = serviceProvider.GetRequiredService<ILogger<UserRepository>>();
            return new UserRepository(cosmosClient, databaseName, logger);
        });

        // Optional: logging/config extras
        // services.AddLogging();
        // services.Configure<MyOptions>(...);
    })
    .Build();

host.Run();