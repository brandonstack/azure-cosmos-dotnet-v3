namespace CosmosBenchmark
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos;

    internal class QueryDatetimeRangeBenchmarkOperation : QueryBenchmarkOperation
    {
        public QueryDatetimeRangeBenchmarkOperation(
            CosmosClient cosmosClient,
            string dbName,
            string containerName,
            string partitionKeyPath,
            string sampleJson) : base(cosmosClient, dbName, containerName, partitionKeyPath, sampleJson)
        {
        }

        public override QueryDefinition QueryDefinition =>
            new QueryDefinition("select * from c where c.UpdateTime>@updatime")
            .WithParameter("updatetime", DateTime.UtcNow.AddDays(-5));

        public override QueryRequestOptions QueryRequestOptions => new QueryRequestOptions
        {
            MaxItemCount = 5,
            PartitionKey = new PartitionKey(this.executionPartitionKey),
        };

        public override bool IsCrossPartitioned => false;

        public override bool IsPaginationEnabled => false;

        public override bool IsQueryStream => true;

        public override async Task PrepareAsync()
        {
            for (int i = 0; i < 10; i++)
            {
                this.executionPartitionKey = Guid.NewGuid().ToString();

                this.sampleJObject["UpdateTime"] = DateTime.UtcNow.AddDays(-i);
                this.sampleJObject[this.partitionKeyPath] = this.executionPartitionKey;

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
