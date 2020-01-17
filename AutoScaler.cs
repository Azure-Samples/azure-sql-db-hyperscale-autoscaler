using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.SQL.DB.Hyperscale.Tools
{
    public class UsageInfo
    {
        public DateTime TimeStamp = DateTime.Now;
        public String ServiceObjective = String.Empty;
        public Decimal AvgCpuPercent = 0;
        public Decimal MovingAvgCpuPercent = 0;
    }

    public enum SearchDirection
    {
        Next,
        Previous
    }

    public static class AutoScaler
    {
        public static Dictionary<string, List<String>> SLOS = new Dictionary<string, List<String>>();

        static AutoScaler()
        {
            var gen4 = new List<String>()
            {
                "hs_gen4_1",
                "hs_gen4_2",
                "hs_gen4_3",
                "hs_gen4_4",
                "hs_gen4_5",
                "hs_gen4_6",
                "hs_gen4_7",
                "hs_gen4_8",
                "hs_gen4_9",
                "hs_gen4_10",
                "hs_gen4_16",
                "hs_gen4_24"
            };

            var gen5 = new List<String>()
            {
                "hs_gen5_2",
                "hs_gen5_4",
                "hs_gen5_6",
                "hs_gen5_8",
                "hs_gen5_10",
                "hs_gen5_12",
                "hs_gen5_14",
                "hs_gen5_16",
                "hs_gen5_18",
                "hs_gen5_20",
                "hs_gen5_24",
                "hs_gen5_32",
                "hs_gen5_40",
                "hs_gen5_80"
            };

            SLOS.Add("hs_gen4", gen4);
            SLOS.Add("hs_gen5", gen5);
        }

        [FunctionName("AutoScaler")]
        public static void Run([TimerTrigger("*/30 * * * * *")]TimerInfo myTimer, ILogger log)
        {
            decimal HighThreshold = 70.0M;
            decimal LowThreshold = 20.0M;

            // TODO: Set min and max vCore

            string connectionString = Environment.GetEnvironmentVariable("AzureSQLConnection");
            string databaseName = (new SqlConnectionStringBuilder(connectionString)).InitialCatalog;

            using (var conn = new SqlConnection(connectionString))
            {
                var result = conn.QuerySingleOrDefault<UsageInfo>(@"
                    select top 1
                        [end_time] as TimeStamp, 
                        databasepropertyex(db_name(), 'ServiceObjective') as ServiceObjective,
                        [avg_cpu_percent] as AvgCpuPercent, 
                        avg([avg_cpu_percent]) over (order by end_time desc rows between current row and 4 following) as MovingAvgCpuPercent
                    from 
                        sys.dm_db_resource_stats
                    order by 
                        end_time desc 
                ");                

                if (result == null) {
                    log.LogInformation("No information received from server.");
                    return;
                }

                // TODO: Store data into Azure Table
                log.LogInformation(JsonConvert.SerializeObject(result));

                // Scale Up
                if (result.MovingAvgCpuPercent > HighThreshold)
                {                    
                    var targetSlo = GetServiceObjective(result.ServiceObjective, SearchDirection.Next);
                    if (targetSlo != null)
                    {
                        log.LogInformation($"HIGH threshold reached: scaling up to {targetSlo}");
                        conn.Execute($"alter database [{databaseName}] modify (service_objective = '{targetSlo}')");
                    }                        
                }

                // Scale Down
                if (result.MovingAvgCpuPercent < LowThreshold)
                {                    
                    var targetSlo = GetServiceObjective(result.ServiceObjective, SearchDirection.Previous);
                    if (targetSlo != null) {                        
                        log.LogInformation($"LOW threshold reached: scaling down to {targetSlo}");
                        conn.Execute($"alter database [{databaseName}] modify (service_objective = '{targetSlo}')");
                    }                        
                }
            }
        }

        public static string GetServiceObjective(string currentServiceObjective, SearchDirection direction)
        {
            var curSlo = currentServiceObjective.ToLower();
            var curGen = curSlo.Substring(0, curSlo.LastIndexOf("_"));

            var availableSlos = SLOS[curGen];
            var index = availableSlos.IndexOf(curSlo);

            if (direction == SearchDirection.Next)
            {
                if (index < availableSlos.Count) 
                    return availableSlos[index+1];
            }
            
            if (direction == SearchDirection.Previous)
            {
                if (index > 0) 
                    return availableSlos[index-1];
            }

            return string.Empty;            
        }
    }
}
