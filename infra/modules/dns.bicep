// The public DNS zone for the Daniel's Dojo domain.
//
// GoDaddy remains the registrar; only DNS hosting moves here, because Static Web Apps can
// serve an apex domain from the global edge only through an ALIAS-class record, which
// GoDaddy's DNS cannot express. Every record the domain had before delegation is
// reproduced here first — a nameserver change makes this zone the only answer, so
// anything missing would simply vanish.

@description('The domain name, e.g. daniels-dojo.com.')
param zoneName string

@description('Static Web App resource id, targeted by the apex alias record.')
param staticWebAppId string

@description('Static Web App default hostname, targeted by the www CNAME.')
param staticWebHostname string

@description('Ownership token Static Web Apps issued for the apex domain. Public, not a secret.')
param apexValidationToken string

@description('Container Apps API FQDN, targeted by the api CNAME.')
param apiFqdn string

@description('Container Apps environment domain verification id, published at asuid.api.')
param apiDomainVerificationId string

@description('DMARC policy carried over from the registrar-hosted zone, so mail handling is unchanged by delegation.')
param dmarcRecord string

// A zone is global; the resource group only decides who administers it.
resource zone 'Microsoft.Network/dnsZones@2018-05-01' = {
  name: zoneName
  location: 'global'
}

// Apex ownership proof for Static Web Apps.
resource apexValidation 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: '@'
  properties: {
    TTL: 3600
    TXTRecords: [{ value: [apexValidationToken] }]
  }
}

// The apex itself. An alias A record resolves to the Static Web App's current addresses
// without pinning a single regional IP, which is what keeps the apex on the global edge.
resource apex 'Microsoft.Network/dnsZones/A@2018-05-01' = {
  parent: zone
  name: '@'
  properties: {
    TTL: 3600
    targetResource: { id: staticWebAppId }
  }
}

resource www 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  parent: zone
  name: 'www'
  properties: {
    TTL: 3600
    CNAMERecord: { cname: staticWebHostname }
  }
}

resource api 'Microsoft.Network/dnsZones/CNAME@2018-05-01' = {
  parent: zone
  name: 'api'
  properties: {
    TTL: 3600
    CNAMERecord: { cname: apiFqdn }
  }
}

// Container Apps proves control of the api subdomain by reading this token.
resource apiVerification 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: 'asuid.api'
  properties: {
    TTL: 3600
    TXTRecords: [{ value: [apiDomainVerificationId] }]
  }
}

// Carried over verbatim from the registrar's zone. The domain sends no mail today (no MX,
// no SPF), but dropping a published DMARC policy during delegation would silently weaken
// how receivers treat spoofed mail from this domain.
resource dmarc 'Microsoft.Network/dnsZones/TXT@2018-05-01' = {
  parent: zone
  name: '_dmarc'
  properties: {
    TTL: 3600
    TXTRecords: [{ value: [dmarcRecord] }]
  }
}

@description('The authoritative nameservers to enter at the registrar.')
output nameServers array = zone.properties.nameServers

@description('The zone name, for confirmation.')
output zoneName string = zone.name
