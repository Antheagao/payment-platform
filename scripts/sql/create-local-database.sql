-- Local bootstrap for docker compose: creates the database and the login the API and worker use.
-- It creates only the database, the login, and the user; every table belongs in an EF Core migration.
-- Runs on every `docker compose up` and is idempotent. Never used outside the local stack: Azure SQL uses
-- Microsoft Entra managed identities and no SQL logins (infra/).
--
-- sqlcmd variables: $(AppPassword), from PAYMENTS_DB_PASSWORD (its docker-compose.yml default, or .env).

SET NOCOUNT ON;

IF DB_ID(N'payments') IS NULL
BEGIN
    CREATE DATABASE payments;
END;
GO

-- The application never connects as sa: it gets one database, not the server.
IF SUSER_ID(N'payments_app') IS NULL
BEGIN
    CREATE LOGIN payments_app WITH PASSWORD = N'$(AppPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = payments;
END
ELSE
BEGIN
    ALTER LOGIN payments_app WITH PASSWORD = N'$(AppPassword)';
END;
GO

USE payments;
GO

IF USER_ID(N'payments_app') IS NULL
BEGIN
    CREATE USER payments_app FOR LOGIN payments_app;
END;

-- db_owner of this database only, because the API applies migrations at startup in the local stack.
-- A deployment splits this: a migration identity owns the schema, the runtime identity reads, writes,
-- and executes procedures.
ALTER ROLE db_owner ADD MEMBER payments_app;
GO
