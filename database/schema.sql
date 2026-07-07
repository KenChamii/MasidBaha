IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FloodReports')
BEGIN
    CREATE TABLE FloodReports (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        Location GEOGRAPHY NOT NULL,
        Severity TINYINT NOT NULL,        -- 1=Passable, 2=KneeLevel, 3=WaistLevel, 4=Impassable
        Notes NVARCHAR(500) NULL,
        PhotoUrl NVARCHAR(500) NULL,
        ReportedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ExpiresAt DATETIME2 NOT NULL,
        ConfidenceScore INT NOT NULL DEFAULT 1,
        Status TINYINT NOT NULL DEFAULT 1, -- 1=Active, 2=Resolved, 3=Expired
        ReporterSessionId NVARCHAR(100) NOT NULL,
        Region NVARCHAR(100) NULL,   -- e.g. "Calabarzon", "National Capital Region" (from reverse geocoding)
        Province NVARCHAR(100) NULL, -- e.g. "Laguna"
        City NVARCHAR(100) NULL      -- e.g. "Cabuyao"
    );
END
GO

-- Backfill columns for databases created before Region/Province/City existed.
IF COL_LENGTH('dbo.FloodReports', 'Region') IS NULL
    ALTER TABLE FloodReports ADD Region NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.FloodReports', 'Province') IS NULL
    ALTER TABLE FloodReports ADD Province NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.FloodReports', 'City') IS NULL
    ALTER TABLE FloodReports ADD City NVARCHAR(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'SIX_FloodReports_Location' AND object_id = OBJECT_ID('FloodReports'))
BEGIN
    CREATE SPATIAL INDEX SIX_FloodReports_Location
        ON FloodReports(Location)
        USING GEOGRAPHY_AUTO_GRID;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FloodReports_Status' AND object_id = OBJECT_ID('FloodReports'))
BEGIN
    CREATE INDEX IX_FloodReports_Status ON FloodReports(Status);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FloodReports_Region_Province_City' AND object_id = OBJECT_ID('FloodReports'))
BEGIN
    CREATE INDEX IX_FloodReports_Region_Province_City ON FloodReports(Region, Province, City) INCLUDE (ConfidenceScore, Status);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ReportVotes')
BEGIN
    CREATE TABLE ReportVotes (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        FloodReportId UNIQUEIDENTIFIER NOT NULL REFERENCES FloodReports(Id),
        VoterSessionId NVARCHAR(100) NOT NULL,
        VoteType TINYINT NOT NULL, -- 1=Confirm, -1=Deny
        VotedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_OneVotePerSessionPerReport UNIQUE (FloodReportId, VoterSessionId)
    );
END
GO