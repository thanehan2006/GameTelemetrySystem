/* =========================================
   2. TAO BANG PLAYERS
   Luu thong tin nguoi choi
========================================= */
USE GameTelemetryDB
IF OBJECT_ID(N'dbo.Players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Players
    (
        PlayerId INT IDENTITY(1,1) PRIMARY KEY,
        PlayerName NVARCHAR(50) NOT NULL,
        CreatedAt DATETIME2 NOT NULL
            DEFAULT SYSDATETIME()
    );
END
GO


/* =========================================
   3. TAO BANG GAMESESSIONS
   Luu thong tin moi lan choi
========================================= */

IF OBJECT_ID(N'dbo.GameSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GameSessions
    (
        SessionId BIGINT IDENTITY(1,1) PRIMARY KEY,

        PlayerId INT NOT NULL,

        StartTime DATETIME2 NOT NULL
            DEFAULT SYSDATETIME(),

        EndTime DATETIME2 NULL,

        Result NVARCHAR(20) NULL,

        CONSTRAINT FK_GameSessions_Players
            FOREIGN KEY (PlayerId)
            REFERENCES dbo.Players(PlayerId)
    );
END
GO


/* =========================================
   4. TAO BANG GAMEEVENTS
   Luu cac su kien trong game
========================================= */

IF OBJECT_ID(N'dbo.GameEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GameEvents
    (
        EventId BIGINT IDENTITY(1,1) PRIMARY KEY,

        SessionId BIGINT NOT NULL,

        EventType NVARCHAR(50) NOT NULL,

        EventTime DATETIME2 NOT NULL
            DEFAULT SYSDATETIME(),

        Value INT NULL,

        CONSTRAINT FK_GameEvents_GameSessions
            FOREIGN KEY (SessionId)
            REFERENCES dbo.GameSessions(SessionId)
    );
END
GO