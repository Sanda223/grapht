param location string

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sampleapp-sql'
  location: location
}
