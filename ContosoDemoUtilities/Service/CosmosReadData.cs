using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace ContosoUtilities.Service
{
    internal class CosmosReadData
    {

        public record Message(string id, string type, string sessionId, string pk, string tenantId,string userId,string name);

        public int ThrottleCount=0;

        public  CosmosClient getCosmosClient(IConfiguration config)
        {
            string endpoint = config["CosmosUri"];
            string key = config["CosmosKey"];


            // Configure CosmosClientOptions
            var clientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    IgnoreNullValues = true,
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                },
                ApplicationName = "ChatApp-LoadTest",
                ConnectionMode = ConnectionMode.Direct,
                ApplicationPreferredRegions = config["ApplicationPreferredRegions"].Split(',').ToList(),

            };

            // Create a CosmosClient using the endpoint and key
            return new CosmosClient(endpoint, key, clientOptions);
        }

        public void readData(CosmosClient client, Container container)
        {
                QueryDefinition query = new QueryDefinition("Select * from c where c.userId= \"u1\" and c.tenantId=\"t1\" and c.type=\"Session\" ");
                PartitionKey partitionKey = new PartitionKey();
                partitionKey = PartitionKey.Null;
                List<Message> sessions = ExecuteQueryAsync<Message>(container, query, partitionKey).GetAwaiter().GetResult();

                if(sessions == null)
                    return;

            int counter = 0;
            foreach (Message session in sessions)
            {
                counter++;
                QueryDefinition query2 = new QueryDefinition("Select * from c where c.type= \"Message\"  and  StartsWith(c.text,\"Sit quidem \",true)");
                    PartitionKey partitionKey2 = new PartitionKey();
                    partitionKey2 = new PartitionKey(session.pk);

                    ExecuteQueryAsync<Message>(container, query2, partitionKey2).GetAwaiter().GetResult();

                if (counter == 25)
                    return;
            }

        }

        private async Task<List<T>> ExecuteQueryAsync<T>(Container container, QueryDefinition query, PartitionKey partitionKey)
        {

            try
            {
                FeedIterator<T> results;

                if (partitionKey == PartitionKey.Null)
                    results = container.GetItemQueryIterator<T>(query);
                else
                    results = container.GetItemQueryIterator<T>(query, null, new QueryRequestOptions() { PartitionKey = partitionKey });


                List<T> output = new();
                while (results.HasMoreResults)
                {
                    FeedResponse<T> response = await results.ReadNextAsync();

                    output.AddRange(response);
                }
                return output;
            }
            catch (CosmosException ex)
            {
                if((int) ex.StatusCode==429)
                {
                    ThrottleCount++;
                }
                return null;
            }

        }

        public async Task<string> getRegionalEndpointAsync(Container container, QueryDefinition query)
        {           
  
            var results = container.GetItemQueryIterator<dynamic>(query);
            string regionalEndpoint = string.Empty;


            while (results.HasMoreResults)
            {
                var response = results.ReadNextAsync().GetAwaiter().GetResult();
                regionalEndpoint = Utility.ExtractTextBetween(response.Diagnostics.ToString(), "rntbd://", ":");

                return regionalEndpoint;
            }

            return null;
        }
    }
}
