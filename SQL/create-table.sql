DROP TABLE IF EXISTS [dbo].[AutoscalerMonitor]
GO

CREATE TABLE [dbo].[AutoscalerMonitor]
(
	[Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
	[InsertedAt] DATETIME2(7) NOT NULL DEFAULT (SYSUTCDATETIME()),
	[CurrentSLO] NVARCHAR(100) NOT NULL DEFAULT (CAST(DATABASEPROPERTYEX(DB_NAME(DB_ID()),'ServiceObjective') AS NVARCHAR(100))),
	[RequestedSLO] NVARCHAR(100) NULL,
	[UsageInfo] NVARCHAR(MAX) NULL CHECK(ISJSON([UsageInfo])=1)
)
GO

DROP TABLE IF EXISTS [dbo].[Numbers]
GO

SELECT TOP (1000000)
	ROW_NUMBER() OVER (ORDER BY A.[object_id]) AS Number,
	RAND(CHECKSUM(NEWID())) AS Random
INTO
	[dbo].[Numbers]
FROM
	sys.[all_columns] a, sys.[all_columns] b
GO

CREATE CLUSTERED INDEX ixc ON dbo.[Numbers](Number)
GO