/* =========================================
   MIGRATION AN TOAN: CHUAN HOA SCHEMA 5 BANG
   Chay script nay tren database GameTelemetryDB hien co
   de nang cap len 5 bang va don gon bang GameEvents
   ========================================= */

USE GameTelemetryDB;
GO

/* 1. Tao va nap du lieu bang Levels */
IF OBJECT_ID(N'dbo.Levels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Levels
    (
        LevelId INT PRIMARY KEY,
        LevelName NVARCHAR(100) NOT NULL,
        BossName NVARCHAR(100) NOT NULL,
        DifficultyMult DECIMAL(4,2) NOT NULL CONSTRAINT DF_Levels_DifficultyMult DEFAULT 1.00
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Levels WHERE LevelId = 1)
    INSERT INTO dbo.Levels (LevelId, LevelName, BossName, DifficultyMult)
    VALUES (1, N'Level 1: Rừng Orc & Mỏ Vàng', N'Đại Tướng Orc Warlord', 1.00);

IF NOT EXISTS (SELECT 1 FROM dbo.Levels WHERE LevelId = 2)
    INSERT INTO dbo.Levels (LevelId, LevelName, BossName, DifficultyMult)
    VALUES (2, N'Level 2: Cao Nguyên Băng Tuyết', N'Frost Colossus', 1.25);

IF NOT EXISTS (SELECT 1 FROM dbo.Levels WHERE LevelId = 3)
    INSERT INTO dbo.Levels (LevelId, LevelName, BossName, DifficultyMult)
    VALUES (3, N'Level 3: Pháo Đài Dung Nham', N'Inferno Overlord', 1.50);
GO

/* 2. Bo sung LevelId va cac cot tong hop cho GameSessions */
IF COL_LENGTH(N'dbo.GameSessions', N'LevelId') IS NULL
    ALTER TABLE dbo.GameSessions ADD LevelId INT NULL;

IF COL_LENGTH(N'dbo.GameSessions', N'EnemiesKilled') IS NULL
    ALTER TABLE dbo.GameSessions ADD EnemiesKilled INT NOT NULL CONSTRAINT DF_GameSessions_EnemiesKilled DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'BossKilled') IS NULL
    ALTER TABLE dbo.GameSessions ADD BossKilled BIT NOT NULL CONSTRAINT DF_GameSessions_BossKilled DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'GoldEarned') IS NULL
    ALTER TABLE dbo.GameSessions ADD GoldEarned INT NOT NULL CONSTRAINT DF_GameSessions_GoldEarned DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'GoldSpent') IS NULL
    ALTER TABLE dbo.GameSessions ADD GoldSpent INT NOT NULL CONSTRAINT DF_GameSessions_GoldSpent DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'BlockHits') IS NULL
    ALTER TABLE dbo.GameSessions ADD BlockHits INT NOT NULL CONSTRAINT DF_GameSessions_BlockHits DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'BlockedDamage') IS NULL
    ALTER TABLE dbo.GameSessions ADD BlockedDamage INT NOT NULL CONSTRAINT DF_GameSessions_BlockedDamage DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'PerfectParries') IS NULL
    ALTER TABLE dbo.GameSessions ADD PerfectParries INT NOT NULL CONSTRAINT DF_GameSessions_PerfectParries DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'TrapHits') IS NULL
    ALTER TABLE dbo.GameSessions ADD TrapHits INT NOT NULL CONSTRAINT DF_GameSessions_TrapHits DEFAULT 0;

IF COL_LENGTH(N'dbo.GameSessions', N'TrapDamage') IS NULL
    ALTER TABLE dbo.GameSessions ADD TrapDamage INT NOT NULL CONSTRAINT DF_GameSessions_TrapDamage DEFAULT 0;
GO

IF OBJECT_ID(N'dbo.FK_GameSessions_Levels', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.GameSessions
        ADD CONSTRAINT FK_GameSessions_Levels
        FOREIGN KEY (LevelId)
        REFERENCES dbo.Levels(LevelId)
        ON DELETE SET NULL;
END
GO

/* 3. Tao bang ShopPurchases */
IF OBJECT_ID(N'dbo.ShopPurchases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShopPurchases
    (
        PurchaseId BIGINT IDENTITY(1,1) PRIMARY KEY,
        SessionId BIGINT NOT NULL,
        ItemType NVARCHAR(20) NOT NULL,
        ItemName NVARCHAR(100) NOT NULL,
        Rarity NVARCHAR(30) NULL,
        Price INT NOT NULL,
        PurchasedAt DATETIME2 NOT NULL CONSTRAINT DF_ShopPurchases_PurchasedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_ShopPurchases_GameSessions
            FOREIGN KEY (SessionId)
            REFERENCES dbo.GameSessions(SessionId)
            ON DELETE CASCADE
    );
END
GO

/* 4. Chuyen du lieu chi tiet tu GameEvents sang GameSessions & ShopPurchases va don gon GameEvents */
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.ShopPurchases)
BEGIN
    INSERT INTO dbo.ShopPurchases (SessionId, ItemType, ItemName, Rarity, Price, PurchasedAt)
    SELECT
        ge.SessionId,
        CASE WHEN ge.EventType = N'weapon_purchased' THEN N'Weapon' ELSE N'Potion' END,
        CASE
            WHEN ge.EventType = N'potion_purchased' THEN N'Bình Hồi Máu Huyết Ngọc'
            WHEN ge.Value = 75  THEN N'Ngân Phong Kiếm'
            WHEN ge.Value = 130 THEN N'Thiên Lam Kiếm'
            WHEN ge.Value = 200 THEN N'Nham Thạch Chiến Chùy'
            WHEN ge.Value = 280 THEN N'Huyết Diễm Đoản Đao'
            WHEN ge.Value = 370 THEN N'Hàn Ngọc Thánh Kiếm'
            WHEN ge.Value = 470 THEN N'Lôi Quang Kim Thương'
            WHEN ge.Value = 590 THEN N'Lưỡi Hái Huyết Nguyệt'
            WHEN ge.Value = 720 THEN N'Phượng Hỏa Thần Kiếm'
            ELSE N'Vũ khí khác (bản cũ)'
        END,
        CASE
            WHEN ge.EventType = N'potion_purchased' THEN N'Tiêu Hao'
            WHEN ge.Value IN (75, 130) THEN N'Hiếm'
            WHEN ge.Value IN (200, 280) THEN N'Sử Thi'
            WHEN ge.Value IN (370, 470) THEN N'Huyền Thoại'
            WHEN ge.Value IN (590, 720) THEN N'Thần Thoại'
            ELSE N'Cũ'
        END,
        ISNULL(ge.Value, 0),
        ge.EventTime
    FROM dbo.GameEvents ge
    WHERE ge.EventType IN (N'weapon_purchased', N'potion_purchased');
END

;WITH EventAgg AS (
    SELECT
        SessionId,
        MAX(CASE WHEN EventType = N'level_start' AND Value IS NOT NULL THEN
            CASE
                WHEN Value BETWEEN 1 AND 3 THEN Value
                WHEN Value BETWEEN 4 AND 6 THEN 2
                WHEN Value BETWEEN 7 AND 9 THEN 3
                ELSE NULL
            END
        END) AS DetectedLevelId,
        SUM(CASE WHEN EventType = N'enemy_killed' THEN 1 ELSE 0 END) AS EnemiesKilled,
        MAX(CASE WHEN EventType = N'boss_killed' THEN 1 ELSE 0 END) AS BossKilled,
        SUM(CASE WHEN EventType = N'gold_collected' THEN ISNULL(Value, 0) ELSE 0 END) AS GoldEarned,
        SUM(CASE WHEN EventType IN (N'weapon_purchased', N'potion_purchased') THEN ISNULL(Value, 0) ELSE 0 END) AS GoldSpent,
        SUM(CASE WHEN EventType = N'block_hit' THEN 1 ELSE 0 END) AS BlockHits,
        SUM(CASE WHEN EventType = N'block_hit' THEN ISNULL(Value, 0) ELSE 0 END) AS BlockedDamage,
        SUM(CASE WHEN EventType = N'perfect_parry' THEN 1 ELSE 0 END) AS PerfectParries,
        SUM(CASE WHEN EventType = N'trap_hit' THEN 1 ELSE 0 END) AS TrapHits,
        SUM(CASE WHEN EventType = N'trap_hit' THEN ISNULL(Value, 0) ELSE 0 END) AS TrapDamage
    FROM dbo.GameEvents
    GROUP BY SessionId
)
UPDATE gs
SET
    gs.LevelId        = COALESCE(gs.LevelId, ea.DetectedLevelId),
    gs.EnemiesKilled  = CASE WHEN gs.EnemiesKilled = 0 THEN ea.EnemiesKilled ELSE gs.EnemiesKilled END,
    gs.BossKilled     = CASE WHEN gs.BossKilled = 0 THEN CAST(ea.BossKilled AS BIT) ELSE gs.BossKilled END,
    gs.GoldEarned     = CASE WHEN gs.GoldEarned = 0 THEN ea.GoldEarned ELSE gs.GoldEarned END,
    gs.GoldSpent      = CASE WHEN gs.GoldSpent = 0 THEN ea.GoldSpent ELSE gs.GoldSpent END,
    gs.BlockHits      = CASE WHEN gs.BlockHits = 0 THEN ea.BlockHits ELSE gs.BlockHits END,
    gs.BlockedDamage  = CASE WHEN gs.BlockedDamage = 0 THEN ea.BlockedDamage ELSE gs.BlockedDamage END,
    gs.PerfectParries = CASE WHEN gs.PerfectParries = 0 THEN ea.PerfectParries ELSE gs.PerfectParries END,
    gs.TrapHits       = CASE WHEN gs.TrapHits = 0 THEN ea.TrapHits ELSE gs.TrapHits END,
    gs.TrapDamage     = CASE WHEN gs.TrapDamage = 0 THEN ea.TrapDamage ELSE gs.TrapDamage END
FROM dbo.GameSessions gs
INNER JOIN EventAgg ea ON gs.SessionId = ea.SessionId;

DELETE FROM dbo.GameEvents
WHERE EventType IN (
    N'enemy_killed',
    N'gold_collected',
    N'block_hit',
    N'perfect_parry',
    N'trap_hit',
    N'item_pickup',
    N'checkpoint_reached',
    N'shop_opened',
    N'weapon_purchased',
    N'potion_purchased'
);

COMMIT TRANSACTION;
GO

/* 5. Tao Index toi uu truy van */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GameSessions_StartTime' AND object_id = OBJECT_ID(N'dbo.GameSessions'))
    CREATE NONCLUSTERED INDEX IX_GameSessions_StartTime ON dbo.GameSessions(StartTime);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GameSessions_PlayerId' AND object_id = OBJECT_ID(N'dbo.GameSessions'))
    CREATE NONCLUSTERED INDEX IX_GameSessions_PlayerId ON dbo.GameSessions(PlayerId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GameSessions_LevelId' AND object_id = OBJECT_ID(N'dbo.GameSessions'))
    CREATE NONCLUSTERED INDEX IX_GameSessions_LevelId ON dbo.GameSessions(LevelId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GameEvents_Session_Type' AND object_id = OBJECT_ID(N'dbo.GameEvents'))
    CREATE NONCLUSTERED INDEX IX_GameEvents_Session_Type ON dbo.GameEvents(SessionId, EventType);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ShopPurchases_SessionId' AND object_id = OBJECT_ID(N'dbo.ShopPurchases'))
    CREATE NONCLUSTERED INDEX IX_ShopPurchases_SessionId ON dbo.ShopPurchases(SessionId);
GO
