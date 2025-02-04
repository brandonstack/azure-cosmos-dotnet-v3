﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// This sample demonstrates how to achieve high performance writes using Azure Comsos DB.
    /// </summary>
    public sealed class Program
    {
        /// <summary>
        /// Main method for the sample.
        /// </summary>
        /// <param name="args">command line arguments.</param>
        public static async Task Main(string[] args)
        {
            try
            {
                BenchmarkConfig config = BenchmarkConfig.From(args);
                await Program.AddAzureInfoToRunSummary();

                ThreadPool.SetMinThreads(config.MinThreadPoolSize, config.MinThreadPoolSize);

                if (config.EnableLatencyPercentiles)
                {
                    TelemetrySpan.IncludePercentile = true;
                    TelemetrySpan.ResetLatencyHistogram(config.ItemCount);
                }

                config.Print();

                Program program = new Program();

                RunSummary runSummary = await program.ExecuteAsync(config);
            }
            finally
            {
                Console.WriteLine($"{nameof(CosmosBenchmark)} completed successfully.");
                if (Debugger.IsAttached)
                {
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadLine();
                }
            }
        }

        private static async Task AddAzureInfoToRunSummary()
        {
            using HttpClient httpClient = new HttpClient();
            using HttpRequestMessage httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "http://169.254.169.254/metadata/instance?api-version=2020-06-01");
            httpRequest.Headers.Add("Metadata", "true");

            try
            {
                using HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequest);
                string jsonVmInfo = await httpResponseMessage.Content.ReadAsStringAsync();
                JObject jObject = JObject.Parse(jsonVmInfo);
                RunSummary.AzureVmInfo = jObject;
                RunSummary.Location = jObject["compute"]["location"].ToString();
                Console.WriteLine($"Azure VM Location:{RunSummary.Location}");
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to get Azure VM info:" + e.ToString());
            }
        }

        /// <summary>
        /// Executing benchmarks for V2/V3 cosmosdb SDK.
        /// </summary>
        /// <returns>a Task object.</returns>
        private async Task<RunSummary> ExecuteAsync(BenchmarkConfig config)
        {
            // V3 SDK client initialization
            using (CosmosClient cosmosClient = config.CreateCosmosClient(config.Key))
            {
                Microsoft.Azure.Cosmos.Database database = cosmosClient.GetDatabase(config.Database);
                if (config.CleanupOnStart)
                {
                    await database.DeleteStreamAsync();
                }

                ContainerResponse containerResponse = await Program.CreatePartitionedContainerAsync(config, cosmosClient);
                Container container = containerResponse;

                int? currentContainerThroughput = await container.ReadThroughputAsync();

                if (!currentContainerThroughput.HasValue)
                {
                    // Container throughput is not configured. It is shared database throughput
                    ThroughputResponse throughputResponse = await database.ReadThroughputAsync(requestOptions: null);
                    throw new InvalidOperationException($"Using database {config.Database} with {throughputResponse.Resource.Throughput} RU/s. " +
                        $"Container {config.Container} must have a configured throughput.");
                }

                Console.WriteLine($"Using container {config.Container} with {currentContainerThroughput} RU/s");
                int taskCount = config.GetTaskCount(currentContainerThroughput.Value);

                Console.WriteLine("Starting Inserts with {0} tasks", taskCount);
                Console.WriteLine();

                string partitionKeyPath = containerResponse.Resource.PartitionKeyPath;
                int opsPerTask = config.ItemCount / taskCount;

                // TBD: 2 clients SxS some overhead
                RunSummary runSummary;

                // V2 SDK client initialization
                Func<IBenchmarkOperation> benchmarkOperationFactory = this.GetBenchmarkFactory(
                    config,
                    partitionKeyPath,
                    cosmosClient);

                if (config.DisableCoreSdkLogging)
                {
                    // Do it after client initialization (HACK)
                    Program.ClearCoreSdkListeners();
                }

                IExecutionStrategy execution = IExecutionStrategy.StartNew(benchmarkOperationFactory);
                runSummary = await execution.ExecuteAsync(config, taskCount, opsPerTask, 0.01);

                if (config.CleanupOnFinish)
                {
                    Console.WriteLine($"Deleting Database {config.Database}");
                    await container.DeleteContainerStreamAsync();
                }

                string consistencyLevel = config.ConsistencyLevel;
                if (string.IsNullOrWhiteSpace(consistencyLevel))
                {
                    AccountProperties accountProperties = await cosmosClient.ReadAccountAsync();
                    consistencyLevel = accountProperties.Consistency.DefaultConsistencyLevel.ToString();
                }
                runSummary.ConsistencyLevel = consistencyLevel;


                if (config.PublishResults)
                {
                    runSummary.Diagnostics = CosmosDiagnosticsLogger.GetDiagnostics();
                    await this.PublishResults(
                        config,
                        runSummary,
                        cosmosClient);
                }

                return runSummary;
            }
        }

        private async Task PublishResults(
            BenchmarkConfig config,
            RunSummary runSummary,
            CosmosClient benchmarkClient)
        {
            if (string.IsNullOrEmpty(config.ResultsEndpoint))
            {
                Container resultContainer = benchmarkClient.GetContainer(
                    databaseId: config.ResultsDatabase ?? config.Database,
                    containerId: config.ResultsContainer);

                await resultContainer.CreateItemAsync(runSummary, new PartitionKey(runSummary.pk));
            }
            else
            {
                using CosmosClient cosmosClient = new CosmosClient(config.ResultsEndpoint, config.ResultsKey);
                Container resultContainer = cosmosClient.GetContainer(config.ResultsDatabase, config.ResultsContainer);
                await resultContainer.CreateItemAsync(runSummary, new PartitionKey(runSummary.pk));
            }
        }

        private Func<IBenchmarkOperation> GetBenchmarkFactory(
            BenchmarkConfig config,
            string partitionKeyPath,
            CosmosClient cosmosClient)
        {
            string sampleItem = File.ReadAllText(config.ItemTemplateFile);

            Type[] availableBenchmarks = Program.AvailableBenchmarks();
            IEnumerable<Type> res = availableBenchmarks
                .Where(e => e.Name.Equals(config.WorkloadType, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(config.WorkloadType + "BenchmarkOperation", StringComparison.OrdinalIgnoreCase));

            if (res.Count() != 1)
            {
                throw new NotImplementedException($"Unsupported workload type {config.WorkloadType}. Available ones are " +
                    string.Join(", \r\n", availableBenchmarks.Select(e => e.Name)));
            }

            ConstructorInfo ci = null;
            object[] ctorArguments = null;
            Type benchmarkTypeName = res.Single();

            ci = benchmarkTypeName.GetConstructor(new Type[] { typeof(CosmosClient), typeof(string), typeof(string), typeof(string), typeof(string) });
            ctorArguments = new object[]
                {
                        cosmosClient,
                        config.Database,
                        config.Container,
                        partitionKeyPath,
                        sampleItem
                };

            if (ci == null)
            {
                throw new NotImplementedException($"Unsupported CTOR for workload type {config.WorkloadType} ");
            }

            return () => (IBenchmarkOperation)ci.Invoke(ctorArguments);
        }

        private static Type[] AvailableBenchmarks()
        {
            Type benchmarkType = typeof(IBenchmarkOperation);
            return typeof(Program).Assembly.GetTypes()
                .Where(p => benchmarkType.IsAssignableFrom(p))
                .ToArray();
        }

        /// <summary>
        /// Get or Create a partitioned container and display cost of running this test.
        /// </summary>
        /// <returns>The created container.</returns>
        private static async Task<ContainerResponse> CreatePartitionedContainerAsync(BenchmarkConfig options, CosmosClient cosmosClient)
        {
            Microsoft.Azure.Cosmos.Database database = await cosmosClient.CreateDatabaseIfNotExistsAsync(options.Database);

            Container container = database.GetContainer(options.Container);

            try
            {
                return await container.ReadContainerAsync();
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Show user cost of running this test
                double estimatedCostPerMonth = 0.06 * options.Throughput;
                double estimatedCostPerHour = estimatedCostPerMonth / (24 * 30);
                Console.WriteLine($"The container will cost an estimated ${Math.Round(estimatedCostPerHour, 2)} per hour (${Math.Round(estimatedCostPerMonth, 2)} per month)");
                // Console.WriteLine("Press enter to continue ...");
                // Console.ReadLine();

                string partitionKeyPath = options.PartitionKeyPath;
                ContainerResponse resp =  await database.CreateContainerAsync(options.Container, partitionKeyPath, options.Throughput);

                // how to make sure it's using default indexing policy?
                //if (options.IndexingPolicy != "Full")
                //{
                //    IndexingPolicies.SetIndexingPolicy(resp, options.IndexingPolicy);
                //    return await cosmosClient.GetContainer(options.Database, options.Container).ReplaceContainerAsync(resp.Resource);
                //}
                return resp;
            }
        }

        private static void ClearCoreSdkListeners()
        {
            Type defaultTrace = Type.GetType("Microsoft.Azure.Cosmos.Core.Trace.DefaultTrace,Microsoft.Azure.Cosmos.Direct");
            TraceSource traceSource = (TraceSource)defaultTrace.GetProperty("TraceSource").GetValue(null);
            traceSource.Switch.Level = SourceLevels.All;
            traceSource.Listeners.Clear();
        }
    }
}
