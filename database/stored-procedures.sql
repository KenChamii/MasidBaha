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