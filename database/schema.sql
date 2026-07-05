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
    ReporterSessionId NVARCHAR(100) NOT NULL
);

CREATE SPATIAL INDEX SIX_FloodReports_Location
    ON FloodReports(Location)
    USING GEOGRAPHY_AUTO_GRID;

CREATE INDEX IX_FloodReports_Status ON FloodReports(Status);

CREATE TABLE ReportVotes (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    FloodReportId UNIQUEIDENTIFIER NOT NULL REFERENCES FloodReports(Id),
    VoterSessionId NVARCHAR(100) NOT NULL,
    VoteType TINYINT NOT NULL, -- 1=Confirm, -1=Deny
    VotedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_OneVotePerSessionPerReport UNIQUE (FloodReportId, VoterSessionId)
);