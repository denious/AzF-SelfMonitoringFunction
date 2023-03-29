using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(SelfMonitoringFunction.Startup))]

namespace SelfMonitoringFunction
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSingleton(provider =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                var storageConnectionString = configuration[Constants.STORAGE_CONNECTION_STRING_NAME];

                var queueClient = new QueueClient(storageConnectionString, Constants.STORAGE_QUEUE_NAME);
                queueClient.CreateIfNotExists();

                return queueClient;
            });

            builder.Services.AddSingleton<EmailServiceSemaphore>();
            builder.Services.AddScoped<IEmailService, DummyEmailService>();
        }
    }
}