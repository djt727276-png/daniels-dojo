// The isolated Daniel's Dojo production environment.
//
// Deployed into its own resource group (daniels-dojo-prod-rg) with its own SQL server and
// database, its own Key Vault, its own registry, its own identities, and its own media
// storage (media.bicep alongside this file). Nothing is shared with development, and no
// development credential works here: every secret is a separate production value set in this
// environment's vault.

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

@description('Full image reference for the API. The same verified digest that passed in dev.')
param apiImage string = ''

@description('False only while the subscription quota blocks a second Container Apps environment (Free Trial allows one). Everything except compute still deploys; flip to true after the subscription upgrade.')
param deployContainerEnvironment bool = true

@description('Whether the Stripe secrets exist in the vault. False keeps commerce Disabled, fail-closed.')
param stripeConfigured bool = false

@description('Whether the video webhook secret exists in the vault. False keeps the video provider Disabled, fail-closed, while still provisioning the app so its hostname exists for creating the provider webhook.')
param videoWebhookConfigured bool = false

@description('Email address that receives budget alerts.')
param budgetAlertEmail string

@description('Production media storage account name, from the prod media.bicep deployment.')
param mediaStorageAccountName string = ''

@description('Exact production browser origins. Extended with the custom domain at cutover.')
param corsOrigins array = []

module platform '../../modules/platform.bicep' = {
  name: 'daniels-dojo-prod-platform'
  params: {
    location: location
    environmentName: 'prod'
    sqlLocation: sqlLocation
    sqlServerName: sqlServerName
    sqlAdminPassword: sqlAdminPassword
    sqlEntraAdminLogin: sqlEntraAdminLogin
    sqlEntraAdminObjectId: sqlEntraAdminObjectId
    corsOrigins: corsOrigins
    apiImage: apiImage
    mediaStorageAccountName: mediaStorageAccountName
    deployApiApp: apiImage != '' && deployContainerEnvironment
    deployContainerEnvironment: deployContainerEnvironment
    stripeConfigured: stripeConfigured
    videoWebhookConfigured: videoWebhookConfigured

    // A paying customer's first request must not be a cold start.
    apiMinReplicas: 1
    monthlyBudgetUsd: 25
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
