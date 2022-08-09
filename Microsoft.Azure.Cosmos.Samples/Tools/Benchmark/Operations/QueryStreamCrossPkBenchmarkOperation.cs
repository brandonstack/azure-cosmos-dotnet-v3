//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace CosmosBenchmark
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;

    internal class QueryStreamCrossPkBenchmarkOperation : QueryBenchmarkOperation
    {
        public QueryStreamCrossPkBenchmarkOperation(
            CosmosClient cosmosClient,
            string dbName,
            string containerName,
            string partitionKeyPath,
            string sampleJson) : base(cosmosClient, dbName, containerName, partitionKeyPath, sampleJson)
        {
        }

        public override QueryDefinition QueryDefinition => new QueryDefinition($"select * from T where T.{FieldValuePath} = @val")
                                                .WithParameter("@val", this.executingFieldValue);

        public override QueryRequestOptions QueryRequestOptions => new QueryRequestOptions()
        {
            PartitionKey = new PartitionKey(this.executionPartitionKey),
            MaxItemCount = 1,
        };

        public override bool IsCrossPartitioned => true;

        public override bool IsPaginationEnabled => false;

        public override bool IsQueryStream => true;

        public override async Task PrepareAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                string objectId = Guid.NewGuid().ToString();
                this.executionPartitionKey = Guid.NewGuid().ToString();
                this.executingFieldValue = "fixed";

                this.sampleJObject["id"] = objectId;
                this.sampleJObject[this.partitionKeyPath] = this.executionPartitionKey;
                this.sampleJObject[FieldValuePath] = this.executingFieldValue;

                using (MemoryStream inputStream = JsonHelper.ToStream(this.sampleJObject))
                {
                    using ResponseMessage itemResponse = await this.container.CreateItemStreamAsync(
                            inputStream,
                            new Microsoft.Azure.Cosmos.PartitionKey(this.executionPartitionKey));

                    System.Buffers.ArrayPool<byte>.Shared.Return(inputStream.GetBuffer());

                    if (itemResponse.StatusCode != HttpStatusCode.Created)
                    {
                        throw new Exception($"Create failed with statuscode: {itemResponse.StatusCode}");
                    }
                }
            }
            this.initialized = true;
        }
    }
}
