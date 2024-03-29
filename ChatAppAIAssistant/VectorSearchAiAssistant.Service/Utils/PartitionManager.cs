using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace VectorSearchAiAssistant.Service.Utils
{

    public static class PartitionManager
    {
        public static PartitionKey GetPK(RecordQueryParams rParams, bool Hierarchial)
        {
            if (rParams.pk == null || rParams.pk == string.Empty)
            {
                if (!Hierarchial)
                    return PartitionKey.Null;
                else
                {
                    PartitionKey partitionKey = new PartitionKeyBuilder()
                       .Add(rParams.tenantId)
                       .Add(rParams.userId)
                       .Build();

                    return partitionKey;
                }
            }
            else
            {
                if (!Hierarchial)
                    return new PartitionKey(rParams.pk);
                else
                {
                    PartitionKey partitionKey = new PartitionKeyBuilder()
                        .Add(rParams.tenantId)
                        .Add(rParams.userId)
                        .Add(rParams.pk)
                        .Build();

                    return partitionKey;
                }
            }
        }

        public class RecordQueryParams
        {
            public string tenantId;
            public string userId;
            public string pk;
            public string documentId;
            public bool enablePaging;

            public RecordQueryParams(bool enablePaging, string tenantId, string userId, string pk, string documentId)
            {
                this.enablePaging = enablePaging;
                this.tenantId = tenantId;
                this.userId = userId;
                this.pk = pk;
                this.documentId = documentId;
            }
        }
    }

    


}
