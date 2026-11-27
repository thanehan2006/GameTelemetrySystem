/* =========================================
   MIGRATION: BO SUNG COT NHAN VAT (CHARACTER ID) CHO BANG GAMESESSIONS
   Luu thong tin tuong ma nguoi choi chon trong moi phien choi
   ========================================= */

USE GameTelemetryDB;
GO

IF OBJECT_ID(N'dbo.GameSessions', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GameSessions', N'CharacterId') IS NULL
BEGIN
    ALTER TABLE dbo.GameSessions ADD CharacterId NVARCHAR(50) NULL;
END
GO

IF OBJECT_ID(N'dbo.GameSessions', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.GameSessions', N'CharacterId') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE name = N'IX_GameSessions_CharacterId'
         AND object_id = OBJECT_ID(N'dbo.GameSessions')
   )
BEGIN
    CREATE NONCLUSTERED INDEX IX_GameSessions_CharacterId
        ON dbo.GameSessions(CharacterId)
        WHERE CharacterId IS NOT NULL;
END
GO
