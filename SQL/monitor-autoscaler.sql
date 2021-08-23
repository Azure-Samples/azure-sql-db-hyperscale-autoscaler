select
    *
from 
    [dbo].[AutoscalerMonitor] as m
cross apply
    openjson(m.UsageInfo) with (
        AvgCpuPercent decimal(9,3),
        MovingAvgCpuPercent decimal(9,3),
        DataPoints int
    ) as u
order by m.InsertedAt desc
