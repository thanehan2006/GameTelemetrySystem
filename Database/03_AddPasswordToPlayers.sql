/* =========================================
   Migration: Them cot PasswordHash cho bang Players
   Chay script nay neu database GameTelemetryDB da duoc tao truoc do
========================================= */
USE GameTelemetryDB;
GO

IF COL_LENGTH(N'dbo.Players', N'PasswordHash') IS NULL
BEGIN
    ALTER TABLE dbo.Players
    ADD PasswordHash NVARCHAR(256) NOT NULL
        CONSTRAINT DF_Players_PasswordHash DEFAULT '';
END
GO
