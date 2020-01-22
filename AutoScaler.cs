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
    public class HyperScaleTier
    {
        private readonly string Name = "hs";
        public int Generation = 5;
        public int Cores = 2;

        public override string ToString()
        {
            return $"{Name}_gen{Generation}_{Cores}";
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;

            if (object.ReferenceEquals(this, obj))
                return true;

            if (this.GetType() != obj.GetType())
                return false;

            return this.ToString() == obj.ToString();
        }

        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        public static bool operator == (HyperScaleTier lhs, HyperScaleTier rhs)
        {
            if (lhs is null)
            {
                if (rhs is null)
                    return true;

                return false;
            }

            return lhs.Equals(rhs);
        }

        public static bool operator != (HyperScaleTier lhs, HyperScaleTier rhs)
        {
            return !(lhs == rhs);
        }

        public static HyperScaleTier Parse(string tierName)
        {
            var curName = tierName.ToLower();
            var parts = curName.Split('_');

            if (parts[0] != "hs") throw new ArgumentException($"'{tierName}' is not an Hyperscale Tier");

            var result = new HyperScaleTier();
            result.Generation = int.Parse(parts[1].Replace("gen", string.Empty));
            result.Cores = int.Parse(parts[2]);

            return result;
        }
    }

    public class UsageInfo
    {
        public DateTime TimeStamp = DateTime.Now;
        public String ServiceObjective = String.Empty;
        public Decimal AvgCpuPercent = 0;
        public Decimal MovingAvgCpuPercent = 0;
        public int DataPoints = 0;
    }

    public enum SearchDirection
    {
        Next,
        Previous
    }

    public class AutoScalerConfiguration
    {
        public int vCoreMin = int.Parse(Environment.GetEnvironmentVariable("vCoreMin"));
        public int vCoreMax = int.Parse(Environment.GetEnvironmentVariable("vCoreMax"));
        public decimal HighThreshold = decimal.Parse(Environment.GetEnvironmentVariable("HighThreshold"));
        public decimal LowThreshold = decimal.Parse(Environment.GetEnvironmentVariable("LowThreshold"));
    }

    public static class AutoScaler
    {
        public static readonly List<String> GEN4 = new List<String>() { "hs_gen4_1", "hs_gen4_2", "hs_gen4_3", "hs_gen4_4", "hs_gen4_5", "hs_gen4_6", "hs_gen4_7", "hs_gen4_8", "hs_gen4_9", "hs_gen4_10", "hs_gen4_16", "hs_gen4_24" };

        public static readonly List<String> GEN5 = new List<String>() { "hs_gen5_2", "hs_gen5_4", "hs_gen5_6", "hs_gen5_8", "hs_gen5_10", "hs_gen5_12", "hs_gen5_14", "hs_gen5_16", "hs_gen5_18", "hs_gen5_20", "hs_gen5_24", "hs_gen5_32", "hs_gen5_40", "hs_gen5_80" };

        public static Dictionary<int, List<String>> HyperscaleSLOs = new Dictionary<int, List<String>>();

        static AutoScaler()
        {
            HyperscaleSLOs.Add(4, GEN4);
            HyperscaleSLOs.Add(5, GEN5);
        }

        [FunctionName("AutoScaler")]
        public static void Run([TimerTrigger("*/15 * * * * *")]TimerInfo myTimer, ILogger log)
        {
            var autoscalerConfig = new AutoScalerConfiguration();

            string connectionString = Environment.GetEnvironmentVariable("AzureSQLConnection");
            string databaseName = (new SqlConnectionStringBuilder(connectionString)).InitialCatalog;

            using (var conn = new SqlConnection(connectionString))
            {
                // TODO: make sure at least 4 data points are collected (1 minute)
                // otherwise autoscaler will always bounce back to lower SLO after increasing it

                var result = conn.QuerySingleOrDefault<UsageInfo>(@"
                    select top 1
                        [end_time] as TimeStamp, 
                        databasepropertyex(db_name(), 'ServiceObjective') as ServiceObjective,
                        [avg_cpu_percent] as AvgCpuPercent, 
                        avg([avg_cpu_percent]) over (order by end_time desc rows between current row and 4 following) as MovingAvgCpuPercent,
                        count(*) over (order by end_time desc rows between current row and 4 following) as DataPoints
                    from 
                        sys.dm_db_resource_stats
                    order by 
                        end_time desc 
                ");

                if (result == null)
                {
                    log.LogInformation("No information received from server.");
                    return;
                }

                // Decode current SLO
                var currentSlo = HyperScaleTier.Parse(result.ServiceObjective);

                // TODO: Write to AppInsight
                log.LogInformation(JsonConvert.SerializeObject(result));

                if (result.DataPoints < 5)
                {
                    log.LogInformation("Not enough data points.");
                    return;
                }

                // Scale Up
                if (result.MovingAvgCpuPercent > autoscalerConfig.HighThreshold)
                {
                    var targetSlo = GetServiceObjective(result.ServiceObjective, SearchDirection.Next);
                    if (targetSlo != null && currentSlo.Cores < autoscalerConfig.vCoreMax && currentSlo != targetSlo)
                    {
                        log.LogInformation($"HIGH threshold reached: scaling up to {targetSlo}");
                        conn.Execute($"alter database [{databaseName}] modify (service_objective = '{targetSlo}')");
                    }
                }

                // Scale Down
                if (result.MovingAvgCpuPercent < autoscalerConfig.LowThreshold)
                {
                    var targetSlo = GetServiceObjective(result.ServiceObjective, SearchDirection.Previous);
                    if (targetSlo != null && currentSlo.Cores > autoscalerConfig.vCoreMin && currentSlo != targetSlo)
                    {
                        log.LogInformation($"LOW threshold reached: scaling down to {targetSlo}");
                        conn.Execute($"alter database [{databaseName}] modify (service_objective = '{targetSlo}')");
                    }
                }
            }
        }

        public static HyperScaleTier GetServiceObjective(string currentServiceObjective, SearchDirection direction)
        {
            var curHS = HyperScaleTier.Parse(currentServiceObjective);
            HyperScaleTier newHS = null;

            var availableSlos = HyperscaleSLOs[curHS.Generation];
            var index = availableSlos.IndexOf(curHS.ToString());

            if (direction == SearchDirection.Next && index < availableSlos.Count)
                    newHS = HyperScaleTier.Parse(availableSlos[index + 1]);

            if (direction == SearchDirection.Previous && index > 0)
                    newHS = HyperScaleTier.Parse(availableSlos[index - 1]);

            return newHS;
        }
    }
}
