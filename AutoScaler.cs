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
            return $"{Name}_gen{Generation}_{Cores}".ToUpper();
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
        public int RequiredDataPoints = int.Parse(Environment.GetEnvironmentVariable("RequiredDataPoints"));
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
        public static void Run([TimerTrigger("*/15 * * * * *")]TimerInfo timer, ILogger log)
        {
            var autoscalerConfig = new AutoScalerConfiguration();

            string connectionString = Environment.GetEnvironmentVariable("AzureSQLConnection");
            string databaseName = (new SqlConnectionStringBuilder(connectionString)).InitialCatalog;

            using (var conn = new SqlConnection(connectionString))
            {
                // Get usage data
                var followingRows = autoscalerConfig.RequiredDataPoints - 1;
                var usageInfo = conn.QuerySingleOrDefault<UsageInfo>($@"
                    select top (1)
                        [end_time] as [TimeStamp], 
                        databasepropertyex(db_name(), 'ServiceObjective') as ServiceObjective,
                        [avg_cpu_percent] as AvgCpuPercent, 
                        avg([avg_cpu_percent]) over (order by end_time desc rows between current row and {followingRows} following) as MovingAvgCpuPercent,
                        count(*) over (order by end_time desc rows between current row and {followingRows} following) as DataPoints
                    from 
                        sys.dm_db_resource_stats
                    order by 
                        end_time desc 
                ");

                // If SLO is happening result could be null
                if (usageInfo == null)
                {
                    log.LogInformation("No information received from server.");
                    return;
                }

                // Decode current SLO
                var currentSlo = HyperScaleTier.Parse(usageInfo.ServiceObjective);
                var targetSlo = currentSlo;

                // At least one minute of historical data is needed
                if (usageInfo.DataPoints < autoscalerConfig.RequiredDataPoints)
                {
                    log.LogInformation("Not enough data points.");
                    WriteMetrics(log, usageInfo, currentSlo, targetSlo);
                    conn.Execute("INSERT INTO [dbo].[AutoscalerMonitor] (RequestedSLO, UsageInfo) VALUES (NULL, @UsageInfo)", new { UsageInfo = JsonConvert.SerializeObject(usageInfo) });
                    return;
                }

                // Scale Up
                if (usageInfo.MovingAvgCpuPercent > autoscalerConfig.HighThreshold)
                {
                    targetSlo = GetServiceObjective(currentSlo, SearchDirection.Next);
                    if (targetSlo != null && currentSlo.Cores < autoscalerConfig.vCoreMax && currentSlo != targetSlo)
                    {
                        log.LogInformation($"HIGH threshold reached: scaling up to {targetSlo}");
                        conn.Execute($"ALTER DATABASE [{databaseName}] MODIFY (SERVICE_OBJECTIVE = '{targetSlo}')");
                    }
                }

                // Scale Down
                if (usageInfo.MovingAvgCpuPercent < autoscalerConfig.LowThreshold)
                {
                    targetSlo = GetServiceObjective(currentSlo, SearchDirection.Previous);
                    if (targetSlo != null && currentSlo.Cores > autoscalerConfig.vCoreMin && currentSlo != targetSlo)
                    {
                        log.LogInformation($"LOW threshold reached: scaling down to {targetSlo}");
                        conn.Execute($"ALTER DATABASE [{databaseName}] MODIFY (SERVICE_OBJECTIVE = '{targetSlo}')");
                    }
                }

                // Write current SLO to monitor table  
                WriteMetrics(log, usageInfo, currentSlo, targetSlo);              
                conn.Execute("INSERT INTO [dbo].[AutoscalerMonitor] (RequestedSLO, UsageInfo) VALUES (@RequestedSLO, @UsageInfo)", new { @RequestedSLO = targetSlo.ToString().ToUpper(), UsageInfo = JsonConvert.SerializeObject(usageInfo) });
            }
        }

        private static void WriteMetrics(ILogger log, UsageInfo usageInfo, HyperScaleTier currentSlo, HyperScaleTier targetSlo)
        {
            log.LogMetric("DataPoints", usageInfo.DataPoints);
            log.LogMetric("AvgCpuPercent", Convert.ToDouble(usageInfo.AvgCpuPercent));
            log.LogMetric("MovingAvgCpuPercent", Convert.ToDouble(usageInfo.MovingAvgCpuPercent));
            log.LogMetric("CurrentCores", Convert.ToDouble(currentSlo.Cores));
            log.LogMetric("TargetCores", Convert.ToDouble(targetSlo.Cores));
        }

        public static HyperScaleTier GetServiceObjective(HyperScaleTier currentSLO, SearchDirection direction)
        {
            var targetSLO = currentSLO;
            var availableSlos = HyperscaleSLOs[currentSLO.Generation];
            var index = availableSlos.IndexOf(currentSLO.ToString());

            if (direction == SearchDirection.Next && index < availableSlos.Count)
                targetSLO = HyperScaleTier.Parse(availableSlos[index + 1]);

            if (direction == SearchDirection.Previous && index > 0)
                targetSLO = HyperScaleTier.Parse(availableSlos[index - 1]);

            return targetSLO;
        }
    }
}
