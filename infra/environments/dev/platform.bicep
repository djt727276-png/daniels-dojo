// The deployed Daniel's Dojo development environment: SQL, Key Vault, monitoring, registry,
// Container Apps, and the Static Web App. Media storage is deployed separately by
// media.bicep, which already exists in this resource group.

targetScope = 'resourceGroup'

@description('Azure region.')
param location string = resourceGroup().location

@description('Region for the SQL logical server, when the group region has no SQL capacity.')
param sqlLocation string = location

@description('SQL logical server name override, for a regional retry after a reserved name.')
param sqlServerName string = ''

@description('SQL administrator password. Supplied at deployment time, stored only in Key Vault.')
@secure()
param sqlAdminPassword string

@description('Entra admin display name for SQL.')
param sqlEntraAdminLogin string

@description('Entra admin object ID for SQL.')
param sqlEntraAdminObjectId string

@description('Full image reference for the API, once one has been pushed.')
param apiImage string = ''

@description('Email address that receives budget alerts.')
param budgetAlertEmail string

module platform '../../modules/platform.bicep' = {
  name: 'daniels-dojo-dev-platform'
  params: {
    location: location
    environmentName: 'dev'
    sqlLocation: sqlLocation
    sqlServerName: sqlServerName
    sqlAdminPassword: sqlAdminPassword
    sqlEntraAdminLogin: sqlEntraAdminLogin
    sqlEntraAdminObjectId: sqlEntraAdminObjectId

    // The local dev server plus the deployed dev frontend. Exact origins, never wildcards.
    corsOrigins: [
      'http://localhost:4200'
      'https://yellow-wave-0ef59fd0f.7.azurestaticapps.net'
    ]

    apiImage: apiImage
    mediaStorageAccountName: 'danielsdojomediadev'
    deployApiApp: apiImage != ''

    monthlyBudgetUsd: 10
    budgetAlertEmail: budgetAlertEmail
  }
}

output sqlServerFqdn string = platform.outputs.sqlServerFqdn
output databaseName string = platform.outputs.databaseName
output keyVaultName string = platform.outputs.keyVaultName
output requiredSecretNames array = platform.outputs.requiredSecretNames
output registryLoginServer string = platform.outputs.registryLoginServer
output apiIdentityPrincipalId string = platform.outputs.apiIdentityPrincipalId
output apiFqdn string = platform.outputs.apiFqdn
output staticWebHostname string = platform.outputs.staticWebHostname
output staticWebName string = platform.outputs.staticWebName
