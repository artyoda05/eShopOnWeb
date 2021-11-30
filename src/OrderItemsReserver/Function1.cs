using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Polly;

namespace OrderItemsReserver
{
    public class Function1
    {
        [FunctionName("Function1")]
        [return: ServiceBus("orderitemsreserver.failed", Connection = "ConnectionStrings:serviceBus")]
        public static async Task<string> Run([ServiceBusTrigger("orderitemsreserver", Connection = "ConnectionStrings:serviceBus")]string myQueueItem, ILogger log, ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", true, true)
                .AddEnvironmentVariables().Build();

            try
            {
                await Policy.Handle<Exception>().WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(10 * retry)).ExecuteAsync(
                    async () =>
                    {
                        var storageAccount = CloudStorageAccount.Parse(config["CloudStorageAccount"]);
            
                        var blobClient = storageAccount.CreateCloudBlobClient();
                        
                        var container = blobClient.GetContainerReference("warehouse-orders");

                        await container.CreateIfNotExistsAsync();
                        
                        var blob = container.GetBlockBlobReference($"{Guid.NewGuid()}.json");
            
                        blob.Properties.ContentType = "application/json";

                        await using var ms = new MemoryStream();
                        var writer = new StreamWriter(ms);
                        await writer.WriteAsync(myQueueItem);
                        await writer.FlushAsync();
                        ms.Position = 0;
                        await blob.UploadFromStreamAsync(ms);
                        await blob.SetPropertiesAsync();
                    });
            }
            catch (Exception)
            {
                return myQueueItem;
            }
            
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");

            return null;
        }
    }
}
