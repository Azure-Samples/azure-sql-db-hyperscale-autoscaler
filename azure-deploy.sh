#!/bin/bash

set -euo pipefail

# Make sure these values are correct for your environment
resourceGroup="azure-sql-db-hs-autoscaler"
appName="azure-sql-db-hs-autoscaler"
location="WestUS2" 

# Change this if you are using your own github repository
gitSource="https://github.com/yorek/azure-sql-hyperscale-autoscaler.git"

# Make sure you have set the connection string for the Azure SQL Hyperscale you want to monitor
if [[ -z "$AzureSQLConnection" ]]; then
	echo "Please set Azure SQL Connection String into AzureSQLConnection variable"
	exit 1
fi
  
fi

az group create \
    -n $resourceGroup \
    -l $location

az functioapp create \
    -g $resourceGroup \
    -n $appName \
    --runtime dotnet \
    --os-type Windows \
    --consumption-plan-location $location \
    --deployment-source-url $gitSource \
    --deployment-source-branch master
    
az webapp config appsettings set \
    -g $resourceGroup \
    -n $appName \
    --settings DefaultConnection=$AzureSQLConnection HighThreshold=70 LowThreshold=20 vCoreMin=2 vCoreMax=8
    
