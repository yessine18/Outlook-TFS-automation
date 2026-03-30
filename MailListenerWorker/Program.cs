
using MailListenerWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddSingleton<AzureDevOpsService>();
        services.AddHostedService<MailPollingService>();
    })
    .Build()
    .Run();