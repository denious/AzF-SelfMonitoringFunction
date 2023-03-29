using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace SelfMonitoringFunction
{
    public class HttpInputFunction
    {
        private const string STORAGE_CONNECTION_STRING_NAME = "AzureWebJobsStorage";
        private const string QUEUE_NAME = "delegated-items";

        private readonly IConfiguration _configuration;

        public HttpInputFunction(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [FunctionName(nameof(HttpInputFunction))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "POST", Route = "{itemCount:int}")]
            HttpRequest request, int itemCount, ILogger log)
        {
            log.LogInformation("Received {itemCount} items", itemCount);

            var storageConnectionString = _configuration[STORAGE_CONNECTION_STRING_NAME];
            var storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            var queueClient = storageAccount.CreateCloudQueueClient();

            var queue = queueClient.GetQueueReference(QUEUE_NAME);
            await queue.CreateIfNotExistsAsync();

            for (var i = 0; i < itemCount; i++)
            {
                var content = "process me";
                var message = new CloudQueueMessage(content);

                await queue.AddMessageAsync(message);
            }

            var resultBody = $"Added {itemCount} items to the queue";
            return new OkObjectResult(resultBody);
        }
    }
}