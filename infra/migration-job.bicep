@description('Name of the existing Container Apps Environment (created by the P1-8a runbook)')
param containerAppsEnvironmentName string

@description('Name of the existing user-assigned managed identity granted db_ddladmin on the target database (see docs/runbooks/p1-8b-azure-deploy-setup.md)')
param migrationsIdentityName string

@description('Full ghcr.io image reference for the efbundle image, e.g. ghcr.io/owner/repo-migrator:<sha>')
param migrationImage string

param location string = resourceGroup().location
param jobName string = 'vlg-migrate'
param sqlServerFqdn string = 'vlg-sqlserver${environment().suffixes.sqlServerHostname}'
param sqlDatabaseName string = 'virtualleadersguide'

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource migrationsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: migrationsIdentityName
}

// No secret material here: the connection string carries only the identity's
// clientId (not a credential by itself) — auth happens via the identity
// attached to this Job below, per ADR-0016. Same Entra-managed-identity
// connection-string shape as $apiConnectionString in
// docs/runbooks/p1-8b-azure-deploy-setup.md step 3 (that one config lives in
// a manually-run runbook, not Bicep, so it can't share this literally).
var migrationsConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${migrationsIdentity.properties.clientId};Encrypt=True;Connect Timeout=60;'

resource migrationJob 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migrationsIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerAppsEnvironment.id
    configuration: {
      triggerType: 'Manual'
      // The free-tier SQL database auto-pauses when idle (ADR-0005) — the
      // first connection of a deploy triggers a ~60s resume, which the
      // default timeout/retry would not tolerate.
      replicaTimeout: 600
      replicaRetryLimit: 2
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: migrationImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          args: [
            '--connection'
            migrationsConnectionString
          ]
        }
      ]
    }
  }
}

output jobName string = migrationJob.name
