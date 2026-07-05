CREATE PROCEDURE sp_InsertFloodReport
    @Lat FLOAT, @Lng FLOAT, @Severity TINYINT,
    @Notes NVARCHAR(500) = NULL, @PhotoUrl NVARCHAR(500) = NULL,
    @ReporterSessionId NVARCHAR(100)
AS
BEGIN
    DECLARE @Id UNIQUEIDENTIFIER = NEWID();
    DECLARE @Point GEOGRAPHY = GEOGRAPHY::Point(@Lat, @Lng, 4326);

    INSERT INTO FloodReports (Id, Location, Severity, Notes, PhotoUrl, ExpiresAt, ReporterSessionId)
    VALUES (@Id, @Point, @Severity, @Notes, @PhotoUrl, DATEADD(HOUR, 6, SYSUTCDATETIME()), @ReporterSessionId);

    SELECT @Id AS Id, @Lat AS Lat, @Lng AS Lng;
END
GO

CREATE PROCEDURE sp_GetNearbyReports
    @Lat FLOAT, @Lng FLOAT, @RadiusMeters INT
AS
BEGIN
    DECLARE @Point GEOGRAPHY = GEOGRAPHY::Point(@Lat, @Lng, 4326);
    SELECT
        Id, Location.Lat AS Lat, Location.Long AS Lng,
        Severity, Notes, PhotoUrl, ReportedAt, ConfidenceScore, Status
    FROM FloodReports
    WHERE Location.STDistance(@Point) <= @RadiusMeters
      AND Status = 1
    ORDER BY ReportedAt DESC;
END
GO

CREATE PROCEDURE sp_VoteOnReport
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

CREATE PROCEDURE sp_ExpireStaleReports
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