IF DB_ID(N'GameTelemetryDB') IS NULL
BEGIN
    CREATE DATABASE GameTelemetryDB;
END
GO

USE GameTelemetryDB;
GO