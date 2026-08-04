// The Daniel's Dojo hosted platform for one environment: SQL, Key Vault, monitoring,
// container registry, the Container Apps environment, the API container app, and the
// Static Web App. Media storage stays in its own module because it carries its own rules.
//
// Every environment is one deployment of this file with different parameters and its own
// resource group. Nothing is shared across environments — separate SQL, separate storage,
// separate vault, separate identities — so an experiment in dev can never touch production.

@description('Azure region.')
param location string = resourceGroup().location

@description('Short environment name used in resource names, e.g. dev or prod.')
@allowed(['dev', 'prod'])
param environmentName string

@description('SQL administrator login for the logical server. The password never appears here.')
param sqlAdminLogin string = 'danielsdojo-sqladmin'

@description('SQL administrator password. Supplied at deployment time, stored only in Key Vault.')
@secure()
param sqlAdminPassword string

@description('Entra admin display name for SQL, e.g. the operator account UPN.')
param sqlEntraAdminLogin string

@description('Entra admin object ID for SQL.')
param sqlEntraAdminObjectId string

@description('Origins the API accepts browser requests from.')
param corsOrigins array

@description('Full image reference the API container app runs, e.g. registry/app:tag.')
param apiImage string

@description('Whether the API container app should be created. False on the first pass, before an image exists.')
param deployApiApp bool = true

@description('Monthly budget in USD that triggers alert emails.')
param monthlyBudgetUsd int = 10

@description('Email address that receives budget alerts.')
param budgetAlertEmail string

var prefix = 'daniels-dojo-${environmentName}'
var registryName = replace('danielsdojo${environmentName}acr', '-', '')
var vaultName = 'dd-${environmentName}-kv-${uniqueString(resourceGroup().id)}'

// ------------------------------------------------------------------ monitoring

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ------------------------------------------------------------------ sql

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: sqlEntraAdminLogin
      sid: sqlEntraAdminObjectId
      azureADOnlyAuthentication: false
    }
  }
}

// The serverless free offer: auto-pause when idle, and in dev the free limit is a hard stop
// rather than a silent overage. Production keeps running past the limit because refusing
// customer traffic to save cents is the wrong trade there.
resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'danielsdojo'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    maxSizeBytes: 34359738368
    autoPauseDelay: 60
    minCapacity: json('0.5')
    useFreeLimit: environmentName == 'dev'
    freeLimitExhaustionBehavior: environmentName == 'dev' ? 'AutoPause' : null
    requestedBackupStorageRedundancy: environmentName == 'prod' ? 'Zone' : 'Local'
    zoneRedundant: false
  }
}

// Azure services (Container Apps outbound) may reach the server; nothing else is opened.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ------------------------------------------------------------------ key vault

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
    publicNetworkAccess: 'Enabled'
  }
}

// ------------------------------------------------------------------ registry

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  sku: { name: 'Basic' }
  properties: {
    // Pull happens with the managed identity via AcrPull; the admin account stays off.
    adminUserEnabled: false
  }
}

// ------------------------------------------------------------------ container apps

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-api-identity'
  location: location
}

// AcrPull for the API identity, scoped to this environment's registry only.
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource apiAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, apiIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    principalId: apiIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Key Vault Secrets User for the API identity, scoped to this environment's vault only.
var vaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource apiVaultRead 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, apiIdentity.id, vaultSecretsUserRoleId)
  scope: vault
  properties: {
    principalId: apiIdentity.properties.principalId
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      vaultSecretsUserRoleId
    )
    principalType: 'ServicePrincipal'
  }
}

// Secret names the API expects. Values are set by the operator or pipeline, never here;
// listing the names in the template is what lets a deployment verify completeness without
// ever seeing a value.
var requiredSecretNames = [
  'sql-connection-string'
  'media-video-token-id'
  'media-video-token-secret'
  'media-video-webhook-secret'
  'media-video-signing-key-id'
  'media-video-signing-key-base64'
  'commerce-stripe-secret-key'
  'commerce-stripe-webhook-secret'
]

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = if (deployApiApp) {
  name: '${prefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        corsPolicy: {
          allowedOrigins: corsOrigins
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
          allowedHeaders: ['authorization', 'content-type']
          allowCredentials: false
        }
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: apiIdentity.id
        }
      ]
      secrets: [
        for name in requiredSecretNames: {
          name: name
          keyVaultUrl: '${vault.properties.vaultUri}secrets/${name}'
          identity: apiIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName == 'prod' ? 'Production' : 'Staging' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'ConnectionStrings__DanielsDojoDatabase', secretRef: 'sql-connection-string' }
            { name: 'Media__Storage__Mode', value: 'Real' }
            { name: 'Media__Video__Mode', value: 'Real' }
            { name: 'Commerce__Stripe__Mode', value: 'Real' }
            { name: 'Media__Video__TokenId', secretRef: 'media-video-token-id' }
            { name: 'Media__Video__TokenSecret', secretRef: 'media-video-token-secret' }
            { name: 'Media__Video__WebhookSecret', secretRef: 'media-video-webhook-secret' }
            { name: 'Media__Video__SigningKeyId', secretRef: 'media-video-signing-key-id' }
            { name: 'Media__Video__SigningKeyBase64', secretRef: 'media-video-signing-key-base64' }
            { name: 'Commerce__Stripe__SecretKey', secretRef: 'commerce-stripe-secret-key' }
            { name: 'Commerce__Stripe__WebhookSecret', secretRef: 'commerce-stripe-webhook-secret' }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: insights.properties.ConnectionString
            }
            { name: 'AZURE_CLIENT_ID', value: apiIdentity.properties.clientId }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
    }
  }
}

// ------------------------------------------------------------------ static web app

resource staticWeb 'Microsoft.Web/staticSites@2023-12-01' = {
  name: '${prefix}-web'
  location: 'eastus2'
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Enabled'
  }
}

// ------------------------------------------------------------------ budget

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: '${prefix}-budget'
  properties: {
    category: 'Cost'
    amount: monthlyBudgetUsd
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-08-01'
    }
    notifications: {
      halfway: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        contactEmails: [budgetAlertEmail]
      }
      nearlyThere: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 90
        contactEmails: [budgetAlertEmail]
      }
      forecastOver: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [budgetAlertEmail]
      }
    }
  }
}

@description('SQL server fully qualified domain name.')
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('Database name.')
output databaseName string = database.name

@description('Key Vault name holding the environment secrets.')
output keyVaultName string = vault.name

@description('Secret names the API expects to exist in the vault.')
output requiredSecretNames array = requiredSecretNames

@description('Registry login server for image pushes.')
output registryLoginServer string = registry.properties.loginServer

@description('API managed identity principal ID, for granting data-plane roles.')
output apiIdentityPrincipalId string = apiIdentity.properties.principalId

@description('API default hostname, once the app exists.')
output apiFqdn string = deployApiApp ? apiApp.properties.configuration.ingress.fqdn : ''

@description('Static Web App default hostname.')
output staticWebHostname string = staticWeb.properties.defaultHostname

@description('Static Web App resource name, for deployment-token retrieval at deploy time.')
output staticWebName string = staticWeb.name
