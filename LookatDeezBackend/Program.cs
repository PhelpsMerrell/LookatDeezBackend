using LookatDeezBackend.Data.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Application Insights (isolated)
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Your DI from old Startup.cs
        services.AddScoped<ICosmosService, CosmosService>();
        services.AddScoped<AuthorizationService>();
      

        // (Optional) logging/config extras here
        // services.AddLogging();
        // services.Configure<MyOptions>(...);
    })
    .Build();

host.Run();
