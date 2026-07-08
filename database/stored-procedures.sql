CREATE OR ALTER PROCEDURE sp_InsertFloodReport
    @Lat FLOAT, @Lng FLOAT, @Severity TINYINT,
    @Notes NVARCHAR(500) = NULL, @PhotoUrl NVARCHAR(500) = NULL,
    @ReporterSessionId NVARCHAR(100),
    @Region NVARCHAR(100) = NULL, @Province NVARCHAR(100) = NULL, @City NVARCHAR(100) = NULL
AS
BEGIN
    DECLARE @Id UNIQUEIDENTIFIER = NEWID();
    DECLARE @Point GEOGRAPHY = GEOGRAPHY::Point(@Lat, @Lng, 4326);

    INSERT INTO FloodReports (Id, Location, Severity, Notes, PhotoUrl, ExpiresAt, ReporterSessionId, Region, Province, City)
    VALUES (@Id, @Point, @Severity, @Notes, @PhotoUrl, DATEADD(HOUR, 6, SYSUTCDATETIME()), @ReporterSessionId, @Region, @Province, @City);

    SELECT @Id AS Id, @Lat AS Lat, @Lng AS Lng;
END
GO

CREATE OR ALTER PROCEDURE sp_GetNearbyReports
    @Lat FLOAT, @Lng FLOAT, @RadiusMeters INT
AS
BEGIN
    DECLARE @Point GEOGRAPHY = GEOGRAPHY::Point(@Lat, @Lng, 4326);
    SELECT
        Id, Location.Lat AS Lat, Location.Long AS Lng,
        Severity, Notes, PhotoUrl, ReportedAt, ConfidenceScore, Status,
        Region, Province, City
    FROM FloodReports
    WHERE Location.STDistance(@Point) <= @RadiusMeters
      AND Status = 1
    ORDER BY ReportedAt DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_GetTopReports
    @Region NVARCHAR(100) = NULL,
    @Province NVARCHAR(100) = NULL,
    @City NVARCHAR(100) = NULL,
    @Limit INT = 20
AS
BEGIN
    SELECT TOP (@Limit)
        Id, Location.Lat AS Lat, Location.Long AS Lng,
        Severity, Notes, PhotoUrl, ReportedAt, ConfidenceScore, Status,
        Region, Province, City
    FROM FloodReports
    WHERE Status = 1
      AND (@Region IS NULL OR Region = @Region)
      AND (@Province IS NULL OR Province = @Province)
      AND (@City IS NULL OR City = @City)
    ORDER BY ConfidenceScore DESC, ReportedAt DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_VoteOnReport
    @FloodReportId UNIQUEIDENTIFIER, @VoterSessionId NVARCHAR(100), @VoteType TINYINT
AS
BEGIN
    INSERT INTO ReportVotes (FloodReportId, VoterSessionId, VoteType)
    VALUES (@FloodReportId, @VoterSessionId, @VoteType);

    UPDATE FloodReports
    SET ConfidenceScore = ConfidenceScore + @VoteType,
        Status = CASE WHEN ConfidenceScore + @VoteType <= 0 THEN 2 ELSE Status END
    WHERE Id = @FloodReportId;

    SELECT ConfidenceScore, Status FROM FloodReports WHERE Id = @FloodReportId;
END
GO

CREATE OR ALTER PROCEDURE sp_GetHeatmapData
    @FromDate DATETIME2 = NULL,
    @ToDate DATETIME2 = NULL,
    @Region NVARCHAR(100) = NULL,
    @Province NVARCHAR(100) = NULL,
    @City NVARCHAR(100) = NULL
AS
BEGIN
    -- Historical view: intentionally does NOT filter by Status, since the
    -- whole point is to include Expired/Resolved reports too (that's the
    -- "pattern over time" the heatmap is meant to reveal). Defaults to the
    -- last 6 months if no range is given, to keep the payload bounded.
    DECLARE @EffectiveFrom DATETIME2 = ISNULL(@FromDate, DATEADD(MONTH, -6, SYSUTCDATETIME()));
    DECLARE @EffectiveTo DATETIME2 = ISNULL(@ToDate, SYSUTCDATETIME());

    SELECT
        Location.Lat AS Lat,
        Location.Long AS Lng,
        Severity,
        Status,
        ReportedAt,
        Region, Province, City
    FROM FloodReports
    WHERE ReportedAt >= @EffectiveFrom
      AND ReportedAt <= @EffectiveTo
      AND (@Region IS NULL OR Region = @Region)
      AND (@Province IS NULL OR Province = @Province)
      AND (@City IS NULL OR City = @City)
    ORDER BY ReportedAt DESC;
END
GO

CREATE OR ALTER PROCEDURE sp_ExpireStaleReports
AS
BEGIN
    DECLARE @ExpiredIds TABLE (Id UNIQUEIDENTIFIER);

    UPDATE FloodReports
    SET Status = 3
    OUTPUT inserted.Id INTO @ExpiredIds
    WHERE Status = 1 AND ExpiresAt <= SYSUTCDATETIME();

    SELECT Id FROM @ExpiredIds;
END
GO

-- ===================== Admin moderation =====================

CREATE OR ALTER PROCEDURE sp_AdminGetReports
    @Status TINYINT = NULL,   -- NULL = all statuses
    @Page INT = 1,
    @PageSize INT = 25
AS
BEGIN
    DECLARE @Offset INT = (@Page - 1) * @PageSize;

    SELECT
        Id, Location.Lat AS Lat, Location.Long AS Lng,
        Severity, Notes, PhotoUrl, ReportedAt, ExpiresAt, ConfidenceScore, Status,
        ReporterSessionId, Region, Province, City
    FROM FloodReports
    WHERE (@Status IS NULL OR Status = @Status)
    ORDER BY ReportedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT COUNT(*) AS TotalCount
    FROM FloodReports
    WHERE (@Status IS NULL OR Status = @Status);
END
GO

CREATE OR ALTER PROCEDURE sp_AdminSetReportStatus
    @FloodReportId UNIQUEIDENTIFIER,
    @Status TINYINT -- 1=Active, 2=Resolved, 3=Expired
AS
BEGIN
    UPDATE FloodReports
    SET Status = @Status
    WHERE Id = @FloodReportId;

    SELECT Id, Status, ConfidenceScore FROM FloodReports WHERE Id = @FloodReportId;
END
GO

CREATE OR ALTER PROCEDURE sp_AdminDeleteReport
    @FloodReportId UNIQUEIDENTIFIER
AS
BEGIN
    -- Votes reference the report via FK, so they must go first.
    DELETE FROM ReportVotes WHERE FloodReportId = @FloodReportId;
    DELETE FROM FloodReports WHERE Id = @FloodReportId;

    SELECT @@ROWCOUNT AS DeletedCount;
END
GO