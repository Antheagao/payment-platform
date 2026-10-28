// Parameters for a dev environment. The Entra group comes from the environment at deployment time, so no
// tenant-specific id is committed; the defaults only let `bicep build-params` validate the file in CI.
using './main.bicep'

param environmentName = 'dev'
param sqlAdministratorGroupName = readEnvironmentVariable('SQL_ADMIN_GROUP_NAME', 'payment-platform-sql-admins')
param sqlAdministratorGroupObjectId = readEnvironmentVariable('SQL_ADMIN_GROUP_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param serviceBusSku = 'Premium'
