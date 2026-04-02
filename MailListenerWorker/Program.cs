using MailListenerWorker;
using MailListenerWorker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddSingleton<AzureDevOpsService>();
        services.AddSingleton<GroqLlmService>();
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JobFieldMappingService>>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            var csvPath = configuration["JobFieldCsv:Path"] ?? "departements.csv";
            return new JobFieldMappingService(logger, csvPath);
        });
        services.AddHostedService<MailPollingService>();
    })
    .Build()
    .Run();