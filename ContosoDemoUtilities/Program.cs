using Microsoft.Extensions.Configuration;
using ContosoUtilities;
using Spectre.Console;
using Console = Spectre.Console.AnsiConsole;
using System.Net;
using System.Net.Quic;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using ContosoUtilities.Service;
using Bogus;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Dynamic;
using System.ComponentModel;
using Microsoft.Azure.Cosmos;
using Bogus.DataSets;
using Microsoft.Azure.Cosmos.Fluent;
using System.Xml.Linq;
using System;
using System.Runtime;
using Bogus.Bson;

namespace ContosoUtilities
{
    internal class Program
    {

        static Faker f=new Faker();

        static async Task Main(string[] args)
        {

            AnsiConsole.Write(
               new FigletText("Contoso Systems")
               .Color(Color.Red));

            Console.WriteLine("");

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                //.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true);

            var config = configuration.Build();                       

           
            const string firewall = "1.\tBlock region for few seconds";
            const string genload = "2.\tLoad test for few seconds";
            const string exit = "3.\tExit this application";


            while (true)
            {

                var selectedOption = AnsiConsole.Prompt(
                      new SelectionPrompt<string>()
                          .Title("Select an option to continue")
                          .PageSize(10)
                          .MoreChoicesText("[grey](Move up and down to reveal more options)[/]")
                          .AddChoices(new[] {
                            firewall,genload, exit
                          }));


                switch (selectedOption)
                {
                    case firewall:
                        AddFirewallRule(config).GetAwaiter().GetResult();
                        break;
                    case genload:
                        GenerateCosmosLoad(config);
                        break;
                    case exit:
                        return;                        
                }
            }                        
        }
                

        private static void GenerateCosmosLoad(IConfiguration configuration)
        {           

            int totalseconds = 60;

            AnsiConsole.Status()
            .Start("Processing...", ctx =>
            {

                ctx.Spinner(Spinner.Known.Star);
                ctx.SpinnerStyle(Style.Parse("green"));

                ctx.Status("Getting Cosmos DB Client Deatils");

               
                string databaseName = configuration["CosmosDatabase"];
                string containerName = configuration["CosmosContainer"];

                //static  singleton approach
                
               CosmosReadData cdbReadData = new CosmosReadData();

               CosmosClient cosmosClient = cdbReadData.getCosmosClient(configuration);  

               var database = cosmosClient?.GetDatabase(databaseName);
               var container = database?.GetContainer(containerName);
               
                //end

                Stopwatch sw = Stopwatch.StartNew();
                sw.Start();

                // Number of threads to use
                int numThreads = 10;
                

                ctx.Status($"Starting {numThreads} threads for ~{totalseconds} seconds");
                int rounds=1;
                do
                {
                    

                    // Create an array to hold the threads
                    Thread[] threads = new Thread[numThreads];

                    for (int i = 0; i < numThreads; i++)
                    {
                        // Use lambda expression to capture unique values of i for each thread
                        int threadIndex = i;
                        threads[i] = new Thread(() =>
                        {
                            //New Client per thread
                            /*
                            CosmosReadData cdbReadData = new CosmosReadData();

                            CosmosClient cClient = cdbReadData.getCosmosClient(configuration);

                            var database = cClient?.GetDatabase(databaseName);
                            var container = database?.GetContainer(containerName);

                            cdbReadData.readData(cClient, container);
                            */
                            //end


                            // Invoke Static CosmosReadData function with parameters
                            cdbReadData.readData(cosmosClient, container);

                            var throttleCount = cdbReadData.ThrottleCount;

                            // Print thread index for identification
                            ctx.Status($"Thread {rounds}#{threadIndex + 1} completed. Threads running for {sw.Elapsed.TotalSeconds} seconds. {throttleCount} requests were throttled");
                        });
                    }

                    // Start all the threads
                    foreach (var thread in threads)
                    {
                        thread.Start();
                    }

                    // Wait for all threads to complete
                    foreach (var thread in threads)
                    {
                        thread.Join();
                    }
                    rounds++;
                } while (sw.Elapsed.TotalSeconds < totalseconds);

        });
    }

        private static async Task AddFirewallRule(IConfiguration configuration)
        {


            AnsiConsole.Status()
                .Start("Processing...", ctx =>
                {

                    ctx.Spinner(Spinner.Known.Star);
                    ctx.SpinnerStyle(Style.Parse("green"));


                    ctx.Status("Getting Cosmos DB Client Deatils");

                    CosmosReadData cdbReadData = new CosmosReadData();

                    CosmosClient cosmosClient = cdbReadData.getCosmosClient(configuration);

                    string databaseName = configuration["CosmosDatabase"];
                    string containerName = configuration["CosmosContainer"];

                    var database = cosmosClient?.GetDatabase(databaseName);
                    var container = database?.GetContainer(containerName);

                    QueryDefinition query = new QueryDefinition($"SELECT TOP 1 * FROM c");

                    string regionalEndpoint = cdbReadData.getRegionalEndpointAsync(container, query).GetAwaiter().GetResult();

                    ctx.Status("Building Firewall Rule");

                    string ipAddress = Dns.GetHostAddresses(regionalEndpoint)[0].ToString();

                    ctx.Status("Adding Firewall Rule");
                    Firewall.AddFirewallRule("Failover Test", ipAddress);

                    int counter = 60;
                    while (counter > 0)
                    {
                        Thread.Sleep(1000);
                        ctx.Status($"FireWall Rule Time Remaining  : {counter} seconds");
                        counter--;
                    }

                    Firewall.RemoveFirewallRule("Failover Test");
                    ctx.Status("Firewall Rule Remove");
                    Thread.Sleep(2 * 1000);
                });
        }   

    }
}