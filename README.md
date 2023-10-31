---
page_type: sample
languages:
- tsql
- csharp
- sql
- json
products:
- azure-sql-database
- azure-functions
- dotnet
- azure
description: "Automatically scale up or down Azure SQL Hyperscale depending on active workload"
urlFragment: "azure-sql-hyperscale-autoscaler"
---

# Azure SQL Hyperscale Autoscaler

![License](https://img.shields.io/badge/license-MIT-green.svg)

<!-- 
Guidelines on README format: https://review.docs.microsoft.com/help/onboard/admin/samples/concepts/readme-template?branch=master

Guidance on onboarding samples to docs.microsoft.com/samples: https://review.docs.microsoft.com/help/onboard/admin/samples/process/onboarding?branch=master

Taxonomies for products and languages: https://review.docs.microsoft.com/new-hope/information-architecture/metadata/taxonomies?branch=master
-->

This is a sample on how autoscaling of Azure SQL DB Hyperscale can be implemented using Azure Functions. The code just uses a simple moving average on the CPU or Workers load for the last minute; if the value is outside minimum or maximum boundaries it will initiate a scale-up or scale-down.

A detailed article related to this repository is available here:

[Autoscaling Azure SQL Hyperscale](https://techcommunity.microsoft.com/t5/azure-sql-database/autoscaling-azure-sql-hyperscale/ba-p/1149025)

Scaling up or down is pretty fast in Hyperscale so responding to workload spikes can be done pretty quickly.

## Deploy

### Azure SQL

Azure Function stores autoscaler data right into the monitored database itself, in the `dbo.AutoscalerMonitor` table. This is useful both to understand how and why the autoscaler took some actions, but also if you want to save historical data to create better autoscaling algorithms. Please use the script `./SQL/create-table` to setup the objects in the target database before running the Azure Function. If you plan to use the autoscaler in a production environment, is recommended to use a different database other than the monitored one to store historical autoscaler data.

The provided script also create a sample `Numbers` table that can be used to execute some load testing to check how the autoscaler works.

Autoscaler data, as an additional sample, is also sent to [Application Insight](https://docs.microsoft.com/en-us/azure/azure-functions/functions-monitoring#log-custom-telemetry-in-c-functions), so autoscler actions can be monitored directly from Azure Portal dashboard.

### Azure Function

Deploy the solution to an Azure Function and then add the following [application settings](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-azure-function-app-settings#settings):

```json
"AzureSQLConnection": "...",
"HighCpuPercent": 70,
"LowCpuPercent": 20,
"HighWorkersPercent": 80,
"LowWorkersPercent": 30,
"vCoreMin": 2,
"vCoreMax": 8,
"RequiredDataPoints": 5
```

- AzureSQLConnection: Connection string to Azure SQL Hyperscale to monitor. Make sure the user used to login to the database has the [right permission](https://docs.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql?view=azuresqldb-current#permissions-1) to run ALTER DATABASE command.
- HighCpuPercent, LowCpuPercent: the minium and maximum CPU threshold values after which scaling up or down will be initiated
- HighWorkersPercent, LowWorkersPercent: the minium and maximum Workers threshold values after which scaling up or down will be initiated
- vCoreMax, vCoreMin: the maximum and minimum number of cores you want to use as limits to scale up and down
- RequiredDataPoints: Number of data points that needs to be gathered before initiating any autoscale action

## Test

To the the autoscaler, if you created the `Numbers` test table, you can run the query `./SQL/load-test.sql` to create some workload. It is suggested that you create a new Hyperscale database with 2vCores to run the test. Tool like [SQL Query Stress](https://github.com/ErikEJ/SqlQueryStress) can be used to execute multiple query in parallel. A sample configuration is available in `SQL` folder: just put the correct connection information and when run it will generate a 80% load on a Gen5 2vCore Hyperscale database. This will be enough to initiate a scale-up action.

## Notes

The solution requires Azure Functions 3.0. If you are using Visual Studio 2019 you don't have to do anything special. If you are using Visual Studio code, read here how to make sure you have Azure Function 3.0 installed on your machine: [Develop Azure Functions using .NET Core 3.1 ](https://dev.to/azure/develop-azure-functions-using-net-core-3-0-gcm)

## Disclaimer

This sample is intended to show how to auto-scale Azure SQL Hyperscale Database, and therefore is not intended to be used in production as is. If you want to use it in production, make sure you correctly understand what is the workload pattern of your database and test if the used moving average can handle it nicely. Unless you have a very predictable and stable workload pattern, it is very likely that a different algorithm other than the simple moving average will be better suited to serve your specific workload. Machine learning can also help here, as it provides solution to the "Demand Forecasting" problem. For example: [Auto-train a time-series forecast model](https://docs.microsoft.com/en-us/azure/machine-learning/how-to-auto-train-forecast), or [ARIMA](https://en.wikipedia.org/wiki/Autoregressive_integrated_moving_average) or more in general: [Demand Forecasting](https://en.wikipedia.org/wiki/Demand_forecasting)

## How to contribute

All contributions are more than welcome. Please refert to the [Code Of Conduct](CODE_OF_CONDUCT.md) to learn what are the basic rules to follow and then fork the repo and start to submit your PR.

