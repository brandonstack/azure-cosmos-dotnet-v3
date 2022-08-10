//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;

    internal class ReadStreamBenchmarkOperation : IBenchmarkOperation
    {
        private readonly Container container;
        private readonly string partitionKeyPath;
        private readonly Dictionary<string, object> sampleJObject;

        private readonly string databsaeName;
        private readonly string containerName;

        protected string nextExecutionItemPartitionKey;
        private string nextExecutionItemId;
        protected bool IsCrossPartition;

        public ReadStreamBenchmarkOperation(
            CosmosClient cosmosClient,
            string dbName,
            string containerName,
            string partitionKeyPath,
            string sampleJson)
        {
            this.databsaeName = dbName;
            this.containerName = containerName;

            this.container = cosmosClient.GetContainer(this.databsaeName, this.containerName);
            this.partitionKeyPath = partitionKeyPath.Replace("/", "");

            this.sampleJObject = JsonHelper.Deserialize<Dictionary<string, object>>(sampleJson);
            this.IsCrossPartition = true;
        }

        public async Task<OperationResult> ExecuteOnceAsync()
        {
            using (ResponseMessage itemResponse = await this.container.ReadItemStreamAsync(
                        this.nextExecutionItemId,
                        new PartitionKey(this.nextExecutionItemPartitionKey)))
            {
                if (itemResponse.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"ReadItem failed wth {itemResponse.StatusCode}");
                }

                return new OperationResult()
                {
                    DatabseName = databsaeName,
                    ContainerName = containerName,
                    RuCharges = itemResponse.Headers.RequestCharge,
                    CosmosDiagnostics = itemResponse.Diagnostics,
                    LazyDiagnostics = () => itemResponse.Diagnostics.ToString(),
                };
            }
        }

        public async Task PrepareAsync()
        {
            this.nextExecutionItemId = Guid.NewGuid().ToString();
            this.nextExecutionItemPartitionKey = this.IsCrossPartition ? Guid.NewGuid().ToString() : "fixed";

            this.sampleJObject["id"] = this.nextExecutionItemId;
            this.sampleJObject[this.partitionKeyPath] = this.nextExecutionItemPartitionKey;

            using (MemoryStream inputStream = JsonHelper.ToStream(this.sampleJObject))
            {
                ResponseMessage itemResponse = await this.container.CreateItemStreamAsync(
                        inputStream,
                        new PartitionKey(this.nextExecutionItemPartitionKey));

                System.Buffers.ArrayPool<byte>.Shared.Return(inputStream.GetBuffer());

                if (itemResponse.StatusCode != HttpStatusCode.Created)
                {
                    throw new Exception($"Create failed with statuscode: {itemResponse.StatusCode}");
                }
            }
        }
    }

    internal class ReadStreamSinglePkBenchmarkOpertation : ReadStreamBenchmarkOperation
    {
        public ReadStreamSinglePkBenchmarkOpertation(
            CosmosClient cosmosClient,
            string dbName,
            string containerName,
            string partitionKeyPath,
            string sampleJson) : base(cosmosClient, dbName, containerName, partitionKeyPath, sampleJson)
        {
            this.IsCrossPartition = false;
        }
    }
}
