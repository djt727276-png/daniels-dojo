// Exact-source media storage for Daniel's Dojo.
//
// This is the account that holds the only verified copy of a course master during the window
// between "uploaded and checked" and "the author deleted their local original". Everything
// below is shaped by that: the account cannot be reached without TLS, blobs are versioned and
// soft-deleted so an accidental overwrite is recoverable, and no key-based access is enabled.
//
// The application never deletes a blob. These settings exist to survive the mistakes the
// application cannot make but a person at a portal still can.

@description('Azure region for the storage account.')
param location string = resourceGroup().location

@description('Globally unique storage account name, 3-24 lowercase alphanumeric characters.')
@minLength(3)
@maxLength(24)
param accountName string

@description('Container holding original uploaded masters.')
param sourceContainerName string = 'media-source'

@description('Days a deleted or overwritten blob version stays recoverable.')
@minValue(7)
@maxValue(365)
param retentionDays int = 30

@description('Origins permitted to upload directly from a browser. Never use a wildcard.')
param allowedUploadOrigins array = []

@description('Principal IDs granted data-plane write access, such as the API managed identity.')
param dataContributorPrincipalIds array = []

// Storage Blob Data Contributor. The API needs this to sign user delegation SAS tokens; it
// deliberately does not get an owner role, and no account key is ever handed out.
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: accountName
  location: location
  sku: {
    // Zone-redundant: the master survives the loss of a single datacentre, which matters
    // precisely because for a while it is the only checked copy.
    name: 'Standard_ZRS'
  }
  kind: 'StorageV2'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true

    // Entra identities only. A shared key cannot be revoked per-caller and turns every leak
    // into a full-account compromise, so the account refuses key auth outright.
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    publicNetworkAccess: 'Enabled'

    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }

    encryption: {
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
      }
      keySource: 'Microsoft.Storage'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    // Versioning plus soft delete is the safety net under a human mistake at the portal. The
    // application has no delete path at all, so these exist for everything else.
    isVersioningEnabled: true

    deleteRetentionPolicy: {
      enabled: true
      days: retentionDays
    }

    containerDeleteRetentionPolicy: {
      enabled: true
      days: retentionDays
    }

    // The browser writes straight to this account, so it needs CORS. Only the methods a
    // single-blob upload actually uses are allowed, and only from named origins.
    cors: {
      corsRules: [
        for origin in allowedUploadOrigins: {
          allowedOrigins: [origin]
          allowedMethods: ['PUT', 'HEAD']
          allowedHeaders: ['x-ms-blob-type', 'content-type']
          exposedHeaders: ['etag']
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

resource sourceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: sourceContainerName
  properties: {
    // Private. A master is reached only through a short-lived signed URL, never by knowing
    // its name.
    publicAccess: 'None'
  }
}

resource dataAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in dataContributorPrincipalIds: {
    name: guid(account.id, principalId, blobDataContributorRoleId)
    scope: account
    properties: {
      principalId: principalId
      roleDefinitionId: subscriptionResourceId(
        'Microsoft.Authorization/roleDefinitions',
        blobDataContributorRoleId
      )
      principalType: 'ServicePrincipal'
    }
  }
]

@description('Account name. Must be configured as Media:Storage:AccountName; it has no default.')
output accountName string = account.name

@description('Container name. Matches the application default, so Media:Storage:SourceContainer only needs setting if this template is deployed with a different sourceContainerName.')
output sourceContainerName string = sourceContainer.name

@description('Blob endpoint, for diagnostics. Not a credential.')
output blobEndpoint string = account.properties.primaryEndpoints.blob
