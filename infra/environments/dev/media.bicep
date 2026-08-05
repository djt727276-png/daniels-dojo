// Development media storage.
//
// Deliberately the smallest thing that lets a real master be uploaded, verified, and played:
// one storage account and one container. Dev and production stay separate accounts, so an
// experiment here can never touch a published course's master.

targetScope = 'resourceGroup'

@description('Azure region.')
param location string = resourceGroup().location

@description('Globally unique storage account name, 3-24 lowercase alphanumeric characters.')
@minLength(3)
@maxLength(24)
param accountName string

@description('Origin the Angular dev server runs on.')
param allowedUploadOrigins array = ['http://localhost:4200']

@description('Principal IDs granted data-plane write access. Empty while running as a developer.')
param dataContributorPrincipalIds array = []

module storage '../../modules/media-storage.bicep' = {
  name: 'media-storage'
  params: {
    location: location
    accountName: accountName
    allowedUploadOrigins: allowedUploadOrigins
    dataContributorPrincipalIds: dataContributorPrincipalIds
  }
}

@description('Set as Media:Storage:AccountName. There is no default for this.')
output accountName string = storage.outputs.accountName

@description('For confirmation only. This matches the application default, so Media:Storage:SourceContainer does not need to be set.')
output sourceContainerName string = storage.outputs.sourceContainerName
