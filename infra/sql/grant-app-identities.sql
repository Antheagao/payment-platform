-- Database users for the App Service managed identities, run once after a deployment by a member of the
-- SQL administrator group (Azure SQL accepts only Microsoft Entra principals; there are no SQL logins).
--
--   sqlcmd -S <sqlServerFqdn output> -d payments --authentication-method ActiveDirectoryDefault \
--     -i infra/sql/grant-app-identities.sql -v ApiIdentity=<apiSiteName output> WorkerIdentity=<workerSiteName output>
--
-- The running apps read and write rows and the worker executes the settlement procedure; neither holds
-- DDL rights. Schema changes run from the pipeline under a separate migration identity.

SET NOCOUNT ON;

IF DATABASE_PRINCIPAL_ID(N'$(ApiIdentity)') IS NULL
    CREATE USER [$(ApiIdentity)] FROM EXTERNAL PROVIDER;
IF DATABASE_PRINCIPAL_ID(N'$(WorkerIdentity)') IS NULL
    CREATE USER [$(WorkerIdentity)] FROM EXTERNAL PROVIDER;

ALTER ROLE db_datareader ADD MEMBER [$(ApiIdentity)];
ALTER ROLE db_datawriter ADD MEMBER [$(ApiIdentity)];
ALTER ROLE db_datareader ADD MEMBER [$(WorkerIdentity)];
ALTER ROLE db_datawriter ADD MEMBER [$(WorkerIdentity)];
GRANT EXECUTE ON SCHEMA::payments TO [$(WorkerIdentity)];
