﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    internal class InsertBenchmarkOperation : IBenchmarkOperation
    {
        private readonly Container container;
        private readonly string partitionKeyPath;
        private readonly Dictionary<string, object> sampleJObject;

        private readonly string databaseName;
        private readonly string containerName;

        protected bool IsCrossPartition;

        public InsertBenchmarkOperation(
            CosmosClient cosmosClient,
            string dbName,
            string containerName,
            string partitionKeyPath,
            string sampleJson)
        {
            this.databaseName = dbName;
            this.containerName = containerName;

            this.container = cosmosClient.GetContainer(this.databaseName, this.containerName);
            this.partitionKeyPath = partitionKeyPath.Replace("/", "");

            this.sampleJObject = JsonHelper.Deserialize<Dictionary<string, object>>(sampleJson);
            this.IsCrossPartition = true;
        }

        public async Task<OperationResult> ExecuteOnceAsync()
        {
            using (MemoryStream input = JsonHelper.ToStream(this.sampleJObject))
            {
                ResponseMessage itemResponse = await this.container.CreateItemStreamAsync(
                        input,
                        new PartitionKey(this.sampleJObject[this.partitionKeyPath].ToString()));

                double ruCharges = itemResponse.Headers.RequestCharge;

                System.Buffers.ArrayPool<byte>.Shared.Return(input.GetBuffer());

                return new OperationResult()
                {
                    DatabseName = databaseName,
                    ContainerName = containerName,
                    RuCharges = ruCharges,
                    CosmosDiagnostics = itemResponse.Diagnostics,
                    LazyDiagnostics = () => itemResponse.Diagnostics.ToString(),
                };
            }
        }

        public Task PrepareAsync()
        {
            if (this.IsCrossPartition)
            {
                string newPartitionKey = Guid.NewGuid().ToString();
                this.sampleJObject[this.partitionKeyPath] = newPartitionKey;
            }
            else
            {
                this.sampleJObject[this.partitionKeyPath] = "fixed";
            }
            this.sampleJObject["id"] = Guid.NewGuid().ToString();

            return Task.CompletedTask;
        }
    }

    internal class InsertSinglePkBenchmarkOperation : InsertBenchmarkOperation
    {
        public InsertSinglePkBenchmarkOperation(
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
