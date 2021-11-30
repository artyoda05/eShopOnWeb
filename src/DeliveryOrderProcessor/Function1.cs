using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DeliveryOrderProcessor
{
    public static class Function1
    {
        [FunctionName("processOrder")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "eShop",
                collectionName: "Orders",
                ConnectionStringSetting = "ConnectionStrings:cosmos",
                CreateIfNotExists = true)]IAsyncCollector<dynamic> documentsOut,
            ILogger log)
        {
            log.LogInformation($"C# Http trigger function executed at: {DateTime.Now}");  
            
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var item = JsonConvert.DeserializeObject(requestBody);

            await documentsOut.AddAsync(item);

            return new OkObjectResult($"Processed order");
        }
    }
}
