namespace CosmosBenchmark.Operations.in
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

internal class QueryStreamSinglePkInKeyBenchmarkOperation : QueryBenchmarkOperation
{
    private readonly List<string> inKeys;
    private string inStr = null;

    public QueryStreamSinglePkInKeyBenchmarkOperation(
        CosmosClient cosmosClient,
        string dbName,
        string containerName,
        string partitionKeyPath,
        string sampleJson) : base(cosmosClient, dbName, containerName, partitionKeyPath, sampleJson)
    {
        this.inKeys = new List<string>(5);
    }

    public override QueryDefinition QueryDefinition =>
        new($"select * from c where c.{FieldValuePath} in ({this.inStr})");

    public override QueryRequestOptions QueryRequestOptions => new QueryRequestOptions
    {
        PartitionKey = new PartitionKey(this.executionPartitionKey),
        MaxItemCount = 5,
    };

    public override bool IsCrossPartitioned => true;

    public override bool IsPaginationEnabled => false;

    public override bool IsQueryStream => true;

    public override async Task PrepareAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            string objectId = Guid.NewGuid().ToString();
            this.executingFieldValue = Guid.NewGuid().ToString();
            this.executionPartitionKey = "fixed";

            this.sampleJObject["id"] = objectId;
            this.sampleJObject[this.partitionKeyPath] = this.executionPartitionKey;
            this.sampleJObject[FieldValuePath] = this.executingFieldValue;

            if (i % 2 == 0)
            {
                this.inKeys.Add(this.executingFieldValue);
            }

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
        this.inStr = string.Join(',', this.inKeys);
        this.initialized = true;
    }
}
}
