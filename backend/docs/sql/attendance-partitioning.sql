-- Optional MSSQL table partitioning for AttendanceLogs (Solution 1).
-- Run manually on production when log volume exceeds ~1M rows per year.
-- Requires AttendanceYear column (added by DbSchemaBootstrap).

/*
-- 1) Partition function (one partition per calendar year)
CREATE PARTITION FUNCTION PF_AttendanceYear (int)
AS RANGE RIGHT FOR VALUES (2024, 2025, 2026, 2027, 2028, 2029, 2030);

-- 2) Partition scheme (all on PRIMARY — adjust filegroups per year for enterprise)
CREATE PARTITION SCHEME PS_AttendanceYear
AS PARTITION PF_AttendanceYear ALL TO ([PRIMARY]);

-- 3) Rebuild clustered index on partition scheme (maintenance window required)
--    Backup first. Test on staging.
CREATE UNIQUE CLUSTERED INDEX CIX_AttendanceLogs_Id
ON AttendanceLogs(Id, AttendanceYear)
ON PS_AttendanceYear(AttendanceYear);
*/

-- Year-based archive view (active vs archived) for reporting
IF OBJECT_ID('vw_AttendanceLogs_All','V') IS NULL
EXEC('CREATE VIEW vw_AttendanceLogs_All AS
  SELECT Id, SyncGuid, EmployeeId, SiteId, DeviceId, PunchTime, AttendanceDate, AttendanceYear,
         PunchType, Source, VerifyMode, IsCorrected, Remarks, SyncedAt, CreatedAt, UpdatedAt, 0 AS IsArchived
  FROM AttendanceLogs
  UNION ALL
  SELECT Id, SyncGuid, EmployeeId, SiteId, DeviceId, PunchTime, AttendanceDate, AttendanceYear,
         PunchType, Source, VerifyMode, IsCorrected, Remarks, SyncedAt, CreatedAt, UpdatedAt, 1 AS IsArchived
  FROM AttendanceLogArchives');
