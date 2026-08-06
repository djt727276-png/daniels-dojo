// Production DNS for daniels-dojo.com, hosted in Azure DNS with GoDaddy as registrar.
//
// Deployed into the production resource group so the zone shares the production
// environment's lifecycle and access control. Every value below is public: hostnames and
// ownership tokens, never a secret.

targetScope = 'resourceGroup'

@description('The domain name.')
param zoneName string = 'daniels-dojo.com'

@description('Production Static Web App name, resolved to a resource id for the apex alias.')
param staticWebAppName string = 'daniels-dojo-prod-web'

@description('Ownership token Static Web Apps issued for the apex domain.')
param apexValidationToken string

@description('Production Container Apps API FQDN.')
param apiFqdn string

@description('Container Apps environment domain verification id.')
param apiDomainVerificationId string

@description('The DMARC policy the registrar-hosted zone published, carried over unchanged.')
param dmarcRecord string = 'v=DMARC1; p=quarantine; adkim=r; aspf=r; rua=mailto:dmarc_rua@onsecureserver.net;'

resource staticWeb 'Microsoft.Web/staticSites@2023-01-01' existing = {
  name: staticWebAppName
}

module dns '../../modules/dns.bicep' = {
  name: 'daniels-dojo-prod-dns'
  params: {
    zoneName: zoneName
    staticWebAppId: staticWeb.id
    staticWebHostname: staticWeb.properties.defaultHostname
    apexValidationToken: apexValidationToken
    apiFqdn: apiFqdn
    apiDomainVerificationId: apiDomainVerificationId
    dmarcRecord: dmarcRecord
  }
}

@description('Enter these at the registrar to delegate DNS hosting.')
output nameServers array = dns.outputs.nameServers
