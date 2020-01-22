# Azure SQL Hyperscale Autoscaler

This is a sample on how autoscaling of Azure SQL DB Hyperscale can be implemented using Azure Functions. 

The code just uses a simple moving average on the CPU load for the last minute; if the value is outside minimum or maximum boundaries it will initiate a scale-up or scale-down. 

Scaling up or down is pretty fast in Hyperscale - usually 30 second or less - so responding to workload spikes can be done pretty quickly.

## Notes

The solution requires Azure Functions 3.0. If you are using Visual Studio 2019 you don't have to do anything special. If you are using Visual Studio code, read here how to make sure you have Azure Function 3.0 installed on your machine: [Develop Azure Functions using .NET Core 3.1 ](https://dev.to/azure/develop-azure-functions-using-net-core-3-0-gcm)

