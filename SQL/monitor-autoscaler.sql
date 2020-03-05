select
    *
from 
    [dbo].[AutoscalerMonitor] as m
cross apply
    openjson(m.UsageInfo) with (
        AvgCpuPercent decimal(9,3),
        MovingAvgCpuPercent decimal(9,3),
        AvgInstanceCpuPercent decimal(9,3),
        MovingAvgInstanceCpuPercent decimal(9,3),
        TimesAbove int,
        TimesBelow int,
        DataPoints int
    ) as u
order by m.InsertedAt desc
