param location string = resourceGroup().location

resource appPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'sampleapp-plan'
  location: location
  sku: {
    name: 'B1'
  }
}

module sqlDatabase 'modules/sql.bicep' = {
  name: 'sqlDeploy'
  params: {
    location: location
  }
}
