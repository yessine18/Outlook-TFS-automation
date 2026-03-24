
using MailListenerWorker;
using Microsoft.Extensions.Hosting;

Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<MailPollingService>();
    })
    .Build()
    .Run();