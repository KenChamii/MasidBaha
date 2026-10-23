/*
  0001_fix_vote_type.sql

  Bug: VoteType was TINYINT and the API sent (byte)-1, which is 255. A "deny"
  vote therefore stored 255 and ADDED 255 to the report's ConfidenceScore.

  This script, safe to re-run:
    1. widens ReportVotes.VoteType to SMALLINT
    2. turns the stored 255s back into -1
    3. recomputes ConfidenceScore for every report that received such a vote
       (initial score 1 + the sum of its votes) and resolves any that drop to <= 0
    4. adds a CHECK so only -1 / 1 can be stored again

  Deploy order: run this script and database/stored-procedures.sql BEFORE the new API build.
*/

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ReportVotes') AND name = 'VoteType' AND system_type_id = 48 -- tinyint
)
BEGIN
    ALTER TABLE dbo.ReportVotes ALTER COLUMN VoteType SMALLINT NOT NULL;
END
GO

BEGIN TRANSACTION;

SELECT DISTINCT FloodReportId INTO #AffectedReports FROM dbo.ReportVotes WHERE VoteType = 255;

UPDATE dbo.ReportVotes SET VoteType = -1 WHERE VoteType = 255;

UPDATE r
SET r.ConfidenceScore = 1 + v.VoteSum
FROM dbo.FloodReports r
JOIN (
    SELECT FloodReportId, SUM(CAST(VoteType AS INT)) AS VoteSum
    FROM dbo.ReportVotes
    GROUP BY FloodReportId
) v ON v.FloodReportId = r.Id
WHERE r.Id IN (SELECT FloodReportId FROM #AffectedReports);

-- Denies that should have resolved the report all along
UPDATE dbo.FloodReports
SET Status = 2
WHERE Id IN (SELECT FloodReportId FROM #AffectedReports)
  AND Status = 1
  AND ConfidenceScore <= 0;

DROP TABLE #AffectedReports;

COMMIT TRANSACTION;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ReportVotes_VoteType')
BEGIN
    ALTER TABLE dbo.ReportVotes
        ADD CONSTRAINT CK_ReportVotes_VoteType CHECK (VoteType IN (-1, 1));
END
GO
