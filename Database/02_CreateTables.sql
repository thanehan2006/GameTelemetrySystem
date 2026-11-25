/* =========================================
   HE THONG TELEMETRY 5 BANG QUAN HE CHUAN HOA
   1. Players       : Tai khoan nguoi choi
   2. Levels        : Danh muc 3 Ban do (1 Map = 1 Level)
   3. GameSessions  : Phien choi + chi so tong hop chien dau & kinh te
   4. ShopPurchases : Giao dich mua vu khi & binh mau tai Lo Ren
   5. GameEvents    : Cac su kien cot moc quan trong (gon, de doc)
   ========================================= */

USE GameTelemetryDB;
GO

/* 1. BANG PLAYERS */
IF OBJECT_ID(N'dbo.Players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Players
    (
        PlayerId INT IDENTITY(1,1) PRIMARY KEY,
        PlayerName NVARCHAR(50) NOT NULL UNIQUE,
        PasswordHash NVARCHAR(256) NOT NULL CONSTRAINT DF_Players_PasswordHash DEFAULT '',
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Players_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

/* 2. BANG LEVELS (1 Map = 1 Level) */
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

/* 3. BANG GAMESESSIONS (Luu phien choi + tong hop chi so chien dau & vang) */
IF OBJECT_ID(N'dbo.GameSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GameSessions
    (
        SessionId BIGINT IDENTITY(1,1) PRIMARY KEY,
        PlayerId INT NOT NULL,
        LevelId INT NULL,
        StartTime DATETIME2 NOT NULL CONSTRAINT DF_GameSessions_StartTime DEFAULT SYSUTCDATETIME(),
        EndTime DATETIME2 NULL,
        Result NVARCHAR(20) NULL,

        EnemiesKilled INT NOT NULL CONSTRAINT DF_GameSessions_EnemiesKilled DEFAULT 0,
        BossKilled BIT NOT NULL CONSTRAINT DF_GameSessions_BossKilled DEFAULT 0,
        GoldEarned INT NOT NULL CONSTRAINT DF_GameSessions_GoldEarned DEFAULT 0,
        GoldSpent INT NOT NULL CONSTRAINT DF_GameSessions_GoldSpent DEFAULT 0,
        BlockHits INT NOT NULL CONSTRAINT DF_GameSessions_BlockHits DEFAULT 0,
        BlockedDamage INT NOT NULL CONSTRAINT DF_GameSessions_BlockedDamage DEFAULT 0,
        PerfectParries INT NOT NULL CONSTRAINT DF_GameSessions_PerfectParries DEFAULT 0,
        TrapHits INT NOT NULL CONSTRAINT DF_GameSessions_TrapHits DEFAULT 0,
        TrapDamage INT NOT NULL CONSTRAINT DF_GameSessions_TrapDamage DEFAULT 0,

        CONSTRAINT FK_GameSessions_Players
            FOREIGN KEY (PlayerId)
            REFERENCES dbo.Players(PlayerId)
            ON DELETE CASCADE,

        CONSTRAINT FK_GameSessions_Levels
            FOREIGN KEY (LevelId)
            REFERENCES dbo.Levels(LevelId)
            ON DELETE SET NULL
    );
END
GO

/* 4. BANG SHOPPURCHASES (Giao dich cua hang Lo Ren) */
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
        ClientEventId NVARCHAR(64) NULL,

        CONSTRAINT FK_ShopPurchases_GameSessions
            FOREIGN KEY (SessionId)
            REFERENCES dbo.GameSessions(SessionId)
            ON DELETE CASCADE
    );
END
GO

/* 5. BANG GAMEEVENTS (Chi luu su kien cot moc quan trong) */
IF OBJECT_ID(N'dbo.GameEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GameEvents
    (
        EventId BIGINT IDENTITY(1,1) PRIMARY KEY,
        SessionId BIGINT NOT NULL,
        EventType NVARCHAR(50) NOT NULL,
        EventTime DATETIME2 NOT NULL CONSTRAINT DF_GameEvents_EventTime DEFAULT SYSUTCDATETIME(),
        Value INT NULL,
        ClientEventId NVARCHAR(64) NULL,

        CONSTRAINT FK_GameEvents_GameSessions
            FOREIGN KEY (SessionId)
            REFERENCES dbo.GameSessions(SessionId)
            ON DELETE CASCADE
    );
END
GO

/* 6. INDEX TOI UU TRUY VAN DASHBOARD */
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

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_GameEvents_ClientEventId' AND object_id = OBJECT_ID(N'dbo.GameEvents'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_GameEvents_ClientEventId ON dbo.GameEvents(ClientEventId) WHERE ClientEventId IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ShopPurchases_ClientEventId' AND object_id = OBJECT_ID(N'dbo.ShopPurchases'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_ShopPurchases_ClientEventId ON dbo.ShopPurchases(ClientEventId) WHERE ClientEventId IS NOT NULL;
GO
