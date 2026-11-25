/* =========================================
   MIGRATION: CHONG GHI DU LIEU TRUNG (IDEMPOTENCY) & DON SACH DU LIEU CU
   1. Them cot ClientEventId va Unique Index cho GameEvents & ShopPurchases
   2. Xoa cac event thua/loi gia tri level khong con dung trong schema 5 bang
   3. Loai bo cac ban ghi su kien cot moc bi ghi trung lap trong cung phien
   4. Dong bo BossKilled cho cac phien Win cu & dong cac phien treo EndTime IS NULL
   ========================================= */

USE GameTelemetryDB;
GO

/* 1. Bo sung cot ClientEventId cho GameEvents & ShopPurchases */
IF COL_LENGTH(N'dbo.GameEvents', N'ClientEventId') IS NULL
BEGIN
    ALTER TABLE dbo.GameEvents ADD ClientEventId NVARCHAR(64) NULL;
END
GO

IF COL_LENGTH(N'dbo.ShopPurchases', N'ClientEventId') IS NULL
BEGIN
    ALTER TABLE dbo.ShopPurchases ADD ClientEventId NVARCHAR(64) NULL;
END
GO

/* 2. Tao Filtered Unique Index chong ghi trung ClientEventId o muc Database */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_GameEvents_ClientEventId' AND object_id = OBJECT_ID(N'dbo.GameEvents'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_GameEvents_ClientEventId
        ON dbo.GameEvents(ClientEventId)
        WHERE ClientEventId IS NOT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ShopPurchases_ClientEventId' AND object_id = OBJECT_ID(N'dbo.ShopPurchases'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_ShopPurchases_ClientEventId
        ON dbo.ShopPurchases(ClientEventId)
        WHERE ClientEventId IS NOT NULL;
END
GO

/* 3. Don du lieu cu, loai bo trung lap va dong bo trang thai phien choi */
BEGIN TRANSACTION;

/* 3a. Xoa event khong nam trong 8 su kien cot moc chuan */
DELETE FROM dbo.GameEvents
WHERE LOWER(LTRIM(RTRIM(EventType))) NOT IN (
    N'login',
    N'game_start',
    N'level_start',
    N'level_complete',
    N'level_failed',
    N'player_dead',
    N'boss_killed',
    N'game_exit'
);

/* 3b. Xoa su kien level/boss/chet co Value khong hop le (ngoai 1..3) */
DELETE FROM dbo.GameEvents
WHERE LOWER(LTRIM(RTRIM(EventType))) IN (
    N'level_start',
    N'level_complete',
    N'level_failed',
    N'player_dead',
    N'boss_killed'
)
AND (Value IS NULL OR Value NOT BETWEEN 1 AND 3);

/* 3c. Loai bo trung lap: nhom su kien chi xuat hien toi da 1 lan moi Session */
;WITH SinglePerSessionDupes AS (
    SELECT
        EventId,
        ROW_NUMBER() OVER (
            PARTITION BY SessionId, LOWER(LTRIM(RTRIM(EventType)))
            ORDER BY EventTime, EventId
        ) AS rn
    FROM dbo.GameEvents
    WHERE LOWER(LTRIM(RTRIM(EventType))) IN (
        N'login',
        N'game_start',
        N'game_exit',
        N'boss_killed',
        N'player_dead'
    )
)
DELETE FROM dbo.GameEvents
WHERE EventId IN (SELECT EventId FROM SinglePerSessionDupes WHERE rn > 1);

/* 3d. Loai bo trung lap: nhom su kien theo tung Level trong cung Session */
;WITH LevelMilestoneDupes AS (
    SELECT
        EventId,
        ROW_NUMBER() OVER (
            PARTITION BY SessionId, LOWER(LTRIM(RTRIM(EventType))), Value
            ORDER BY EventTime, EventId
        ) AS rn
    FROM dbo.GameEvents
    WHERE LOWER(LTRIM(RTRIM(EventType))) IN (
        N'level_start',
        N'level_complete',
        N'level_failed'
    )
)
DELETE FROM dbo.GameEvents
WHERE EventId IN (SELECT EventId FROM LevelMilestoneDupes WHERE rn > 1);

/* 3e. Dong bo BossKilled = 1 va su kien boss_killed cho cac phien Win cu bi thieu do bug thu tu OnDied */
UPDATE dbo.GameSessions
SET BossKilled = 1
WHERE LOWER(LTRIM(RTRIM(ISNULL(Result, N'')))) IN (N'win', N'victory', N'completed')
  AND BossKilled = 0;

INSERT INTO dbo.GameEvents (SessionId, EventType, EventTime, Value)
SELECT
    gs.SessionId,
    N'boss_killed',
    COALESCE(gs.EndTime, gs.StartTime),
    gs.LevelId
FROM dbo.GameSessions gs
WHERE LOWER(LTRIM(RTRIM(ISNULL(gs.Result, N'')))) IN (N'win', N'victory', N'completed')
  AND gs.LevelId BETWEEN 1 AND 3
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.GameEvents ge
      WHERE ge.SessionId = gs.SessionId
        AND LOWER(LTRIM(RTRIM(ge.EventType))) = N'boss_killed'
  );

/* 3f. Dong cac phien choi treo qua 1 gio ma chua co EndTime */
UPDATE gs
SET
    gs.EndTime = COALESCE(
        (SELECT MAX(ge.EventTime) FROM dbo.GameEvents ge WHERE ge.SessionId = gs.SessionId),
        gs.StartTime
    ),
    gs.Result = COALESCE(NULLIF(LTRIM(RTRIM(gs.Result)), N''), N'Quit')
FROM dbo.GameSessions gs
WHERE gs.EndTime IS NULL
  AND gs.StartTime < DATEADD(HOUR, -1, SYSUTCDATETIME());

COMMIT TRANSACTION;
GO

/* 4. Dong bo khoa ngoai ON DELETE CASCADE cho GameSessions -> Players va GameEvents -> GameSessions */
IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_GameSessions_Players'
      AND parent_object_id = OBJECT_ID(N'dbo.GameSessions')
      AND delete_referential_action_desc <> N'CASCADE'
)
BEGIN
    ALTER TABLE dbo.GameSessions DROP CONSTRAINT FK_GameSessions_Players;
END
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_GameSessions_Players'
      AND parent_object_id = OBJECT_ID(N'dbo.GameSessions')
)
BEGIN
    ALTER TABLE dbo.GameSessions
        ADD CONSTRAINT FK_GameSessions_Players
        FOREIGN KEY (PlayerId)
        REFERENCES dbo.Players(PlayerId)
        ON DELETE CASCADE;
END
GO

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_GameEvents_GameSessions'
      AND parent_object_id = OBJECT_ID(N'dbo.GameEvents')
      AND delete_referential_action_desc <> N'CASCADE'
)
BEGIN
    ALTER TABLE dbo.GameEvents DROP CONSTRAINT FK_GameEvents_GameSessions;
END
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_GameEvents_GameSessions'
      AND parent_object_id = OBJECT_ID(N'dbo.GameEvents')
)
BEGIN
    ALTER TABLE dbo.GameEvents
        ADD CONSTRAINT FK_GameEvents_GameSessions
        FOREIGN KEY (SessionId)
        REFERENCES dbo.GameSessions(SessionId)
        ON DELETE CASCADE;
END
GO
