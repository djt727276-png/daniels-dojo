// Production media storage. Completely separate from development: its own account, its own
// container, its own RBAC. The same module supplies the same guarantees already verified in
// development — versioning, soft delete, no shared keys, no public access, HTTPS only.

targetScope = 'resourceGroup'

@description('Azure region.')
param location string = resourceGroup().location

@description('Globally unique storage account name, 3-24 lowercase alphanumeric characters.')
@minLength(3)
@maxLength(24)
param accountName string

@description('Exact production browser origins allowed to upload. Never a wildcard.')
param allowedUploadOrigins array

@description('The production API managed identity, granted data-plane access.')
param dataContributorPrincipalIds array

@description('Production keeps deleted masters recoverable for longer than development.')
param retentionDays int = 90

module storage '../../modules/media-storage.bicep' = {
  name: 'media-storage'
  params: {
    location: location
    accountName: accountName
    allowedUploadOrigins: allowedUploadOrigins
    dataContributorPrincipalIds: dataContributorPrincipalIds
    retentionDays: retentionDays
  }
}

@description('Set as Media:Storage:AccountName in the production configuration.')
output accountName string = storage.outputs.accountName

@description('Matches the application default; listed for confirmation only.')
output sourceContainerName string = storage.outputs.sourceContainerName
