using LookatDeezBackend.Data.Repositories;
using LookatDeezBackend.Data.Services;
using LookatDeezBackend.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Cosmos;
using LookatDeezBackend.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseMiddleware<JwtAuthenticationMiddleware>();
    })
    .ConfigureOpenApi()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Singleton CosmosClient from env var
        services.AddSingleton<CosmosClient>(_ =>
        {
            var cs = Environment.GetEnvironmentVariable("CosmosConnectionString");
            if (string.IsNullOrEmpty(cs))
                throw new InvalidOperationException("CosmosConnectionString env var is not set");
            return new CosmosClient(cs);
        });

        // Repositories via DI
        services.AddScoped<IUserRepository>(sp =>
        {
            var client = sp.GetRequiredService<CosmosClient>();
            var dbName = Environment.GetEnvironmentVariable("CosmosDb_DatabaseName") ?? "lookatdeez-db";
            var logger = sp.GetRequiredService<ILogger<UserRepository>>();
            return new UserRepository(client, dbName, logger);
        });

        services.AddScoped<IFriendRequestRepository>(sp =>
        {
            var client = sp.GetRequiredService<CosmosClient>();
            var dbName = Environment.GetEnvironmentVariable("CosmosDb_DatabaseName") ?? "lookatdeez-db";
            return new FriendRequestRepository(client, dbName);
        });

        // CosmosService resolved by constructor injection (no factory lambda needed)
        services.AddScoped<ICosmosService, CosmosService>();

        services.AddScoped<AuthorizationService>();
    })
    .Build();


host.Run();