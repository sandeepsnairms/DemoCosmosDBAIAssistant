using Azure;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorSearchAiAssistant.Service.Services;

namespace VectorSearchAiAssistant.Service.Utils
{
    internal class CosmosMetricStore
    {
        string metricName;
        
        double totalRequestCharge = 0;
        string statusCode = string.Empty;
        Container metricsContainer;
        // Time the query
        Stopwatch stopWatch = new Stopwatch();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        public CosmosMetricStore(string MetricName, Container MetricsContainer)
        {
            metricName= MetricName;
            metricsContainer=  MetricsContainer;

            stopWatch.Start();
        }

        public void UpdateResponse<T>(Microsoft.Azure.Cosmos.Response<T> response)
        {            
            string region = GetRegionFromDiag(response.Diagnostics);
            statusCode = string.IsNullOrWhiteSpace(region) ? statusCode : ((int)response.StatusCode).ToString() + "-" + region;
            totalRequestCharge += response.RequestCharge;
        }

        public void UpdateResponse(TransactionalBatchResponse response)
        {
            string region = GetRegionFromDiag(response.Diagnostics);
            statusCode = string.IsNullOrWhiteSpace(region) ? statusCode : ((int)response.StatusCode).ToString() + "-" + region;
            totalRequestCharge += response.RequestCharge;
        }

        public  void UpdateResponse<T>(FeedResponse<T> response)
        {
            string region = GetRegionFromDiag(response.Diagnostics);
            statusCode = string.IsNullOrWhiteSpace(region) ? statusCode : ((int)response.StatusCode).ToString() + "-" + region;
            totalRequestCharge += response.RequestCharge;
        }

        public void StoreSucessMetric()
        {
            stopWatch.Stop();
               
            TimeSpan ts = stopWatch.Elapsed;

            InsertCDBMetricsAsync(ts);

        }

        public void StoreExceptionMetric(CosmosException ex)
        {
            stopWatch.Stop();

            TimeSpan ts = stopWatch.Elapsed;

            totalRequestCharge = ex.RequestCharge;
            statusCode = (int)ex.StatusCode + "-" + ex.StatusCode;

            InsertCDBMetricsAsync(ts);
        }


        /// <summary>
        /// Insert Metrics for Cosmos requests.
        /// </summary>
        private void InsertCDBMetricsAsync(TimeSpan ts)
        {
            string today = new(System.DateTime.Today.ToString());
            PartitionKey partitionKey = new(today);

            var metric = new CDBMetric(Guid.NewGuid().ToString(), today, metricName, totalRequestCharge, statusCode, ts.TotalMilliseconds, DateTime.Now);

            Task.Run(() => metricsContainer?.CreateItemAsync(
                item: metric,
                partitionKey: partitionKey
            ));
        }


        private string GetRegionFromDiag(CosmosDiagnostics diagnostics)
        {

            var regionList = diagnostics.GetContactedRegions();

            string returnval = string.Empty;
            if (regionList != null)
            {
                foreach ((string region, Uri uri) regionname in regionList)
                {
                    returnval = returnval + "(" +regionname.region +")";
                }
            }

            return returnval;
        }

        private record CDBMetric(
           string id,
           string pk,
           string metricName,
           double RU,
           string StatusCode,
           double Latency,
           DateTime TimeStamp
       );
    }

    

   
}
