# Azure SQL Hyperscale Autoscaler

This is a sample on how autoscaling of Azure SQL DB Hyperscale can be implemented using Azure Functions. 

The code just uses a simple moving average on the CPU load for the last minute; if the value is outside minimum or maximum boundaries it will initiate a scale-up or scale-down.

Scaling up or down is pretty fast in Hyperscale - usually 15 second or less - so responding to workload spikes can be done pretty quickly.

## Deploy

### Azure SQL

While this is really not needed, for this sample the Azure Function is storing data into the monitored database itself, so you can track over time when, how and why the autoscaler took some actions. Please use the script `./SQL/create-table` to setup the objects in the target database before running the Azure Function.

The script also create a sample `Numbers` table that can be used to execute some load testing to check how the autoscaler works.

In a production environment you may want to take advantage of [Application Insight](https://docs.microsoft.com/en-us/azure/azure-functions/functions-monitoring#log-custom-telemetry-in-c-functions) to store historical autoscaler data, so that it would make easier and cheaper the long term storage of such data and the creation of a dashboard using the Azure Portal.

### Azure Function

Deploy the solution to an Azure Function and then add the following [application settings](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-azure-function-app-settings#settings):

```json
"AzureSQLConnection": "...",
"HighThreshold": 70,
"LowThreshold": 20,
"vCoreMin": 2,
"vCoreMax": 8,
"RequiredDataPoints": 5
```

- AzureSQLConnection: Connection string to Azure SQL Hyperscale to monitor. Make sure the user used to login to the database has the [right permission](https://docs.microsoft.com/en-us/sql/t-sql/statements/alter-database-transact-sql?view=azuresqldb-current#permissions-1) to run ALTER DATABASE command.
- HighThreshold, LowThreshold: the minium and maximum threshold values after which scaling up or down will be initiated
- vCoreMax, vCoreMin: the maximum and minimum number of cores you want to use as limits to scale up and down
- RequiredDataPoints: Number of data points that needs to be gathered before initiating any autoscale action

## Test

To the the autoscaler, if you created the `Numbers` test table, you can run the query `./SQL/load-test.sql` to create some workload. It is suggested that you create a new Hyperscale database with 2vCores to run the test. Tool like [SQL Query Stress](https://github.com/ErikEJ/SqlQueryStress) can be used to execute multiple query in parallel. A sample configuration is available in `SQL` folder: just put the correct connection information and when run it will generate a 75% load on a Gen5 2vCore Hyperscale database.

## Notes

The solution requires Azure Functions 3.0. If you are using Visual Studio 2019 you don't have to do anything special. If you are using Visual Studio code, read here how to make sure you have Azure Function 3.0 installed on your machine: [Develop Azure Functions using .NET Core 3.1 ](https://dev.to/azure/develop-azure-functions-using-net-core-3-0-gcm)

## Disclaimer

This sample is intended to show how to autoscale Azure SQL Hyperscale Database, and therefore is not intended to be used in production as is. If you want to use it in production, make sure you correctly determine the correct time window to be used to gather usage data, so that it will correctly represent your workload. Also, a different algorithm other than the simple moving average could be better suited to serve your specific workload. 

## How to contribute

All contributions are more than welcome. Please refert to the [Code Of Conduct](CODE_OF_CONDUCT.md) to learn what are the basic rules to follow and then fork the repo and start to submit your PR.