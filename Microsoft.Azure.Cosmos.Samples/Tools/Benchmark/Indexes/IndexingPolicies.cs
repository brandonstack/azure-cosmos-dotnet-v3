namespace CosmosBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Antlr4.Runtime;
    using Microsoft.Azure.Cosmos;
    using Index = Microsoft.Azure.Cosmos.Index;

    internal enum IndexingChoice
    {
        None,
        Full,
        DateTime,
        Patial,
        Eq,
    }

    internal static class IndexingPolicies
    {
        public static void SetIndexingPolicy(ContainerResponse containerResponse, string policyName)
        {
            if (Enum.TryParse<IndexingChoice>(policyName, out IndexingChoice policy))
            {
                IndexingPolicy curPolicy = containerResponse.Resource.IndexingPolicy;
                switch (policy)
                {
                    case IndexingChoice.None:
                        curPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/*" });
                        break;
                    case IndexingChoice.Full:
                        curPolicy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
                        break;
                    case IndexingChoice.DateTime:
                        curPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/*" });
                        curPolicy.IncludedPaths.Add(new IncludedPath
                        {
                            Path = "/UpdateTime",
                            Indexes = new System.Collections.ObjectModel.Collection<Index> { Index.Range(DataType.String) },
                        });
                        break;
                    case IndexingChoice.Patial:
                        break;
                    case IndexingChoice.Eq:
                        curPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/*" });
                        curPolicy.IncludedPaths.Add(new IncludedPath
                        {
                            Path = "/partitionKey",
                            Indexes = new System.Collections.ObjectModel.Collection<Index> { Index.Hash(DataType.String) },
                        });
                        break;
                    default:
                        break;
                }
            }
            throw new InvalidOperationException(nameof(policyName));
        }
    }
}
