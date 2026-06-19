using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Data;

/// <summary>Applies performance schema upgrades to existing databases (EnsureCreated does not alter tables).</summary>
public static class DbSchemaBootstrap
{
    public static async Task ApplyPerformanceSchemaAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            // Each statement runs in its own batch — SQL Server cannot reference new columns in the same batch as ALTER.
            await Exec(db, """
                IF COL_LENGTH('AttendanceLogs','AttendanceDate') IS NULL
                  ALTER TABLE AttendanceLogs ADD AttendanceDate date NOT NULL CONSTRAINT DF_AttendanceLogs_Date DEFAULT '2000-01-01';
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('AttendanceLogs','AttendanceYear') IS NULL
                  ALTER TABLE AttendanceLogs ADD AttendanceYear int NOT NULL CONSTRAINT DF_AttendanceLogs_Year DEFAULT 2000;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('PayrollCutoffs','ProcessingError') IS NULL
                  ALTER TABLE PayrollCutoffs ADD ProcessingError nvarchar(max) NULL;
                """, ct);

            await Exec(db, """
                IF OBJECT_ID('AttendanceDailySummaries','U') IS NULL
                CREATE TABLE AttendanceDailySummaries (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  EmployeeId int NOT NULL,
                  AttendanceDate date NOT NULL,
                  AttendanceYear int NOT NULL,
                  TimeIn datetime2 NULL,
                  TimeOut datetime2 NULL,
                  BreakOut datetime2 NULL,
                  BreakIn datetime2 NULL,
                  HoursWorked decimal(18,2) NOT NULL DEFAULT 0,
                  LateMinutes decimal(18,2) NOT NULL DEFAULT 0,
                  UndertimeMinutes decimal(18,2) NOT NULL DEFAULT 0,
                  Status nvarchar(32) NOT NULL DEFAULT 'Absent',
                  HasTimeIn bit NOT NULL DEFAULT 0,
                  ComputedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL,
                  CONSTRAINT FK_AttendanceDailySummaries_Employees FOREIGN KEY (EmployeeId) REFERENCES Employees(Id)
                );
                """, ct);

            await Exec(db, """
                IF OBJECT_ID('AttendanceLogArchives','U') IS NULL
                CREATE TABLE AttendanceLogArchives (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  SyncGuid uniqueidentifier NOT NULL,
                  EmployeeId int NOT NULL,
                  SiteId int NULL,
                  DeviceId int NULL,
                  PunchTime datetime2 NOT NULL,
                  AttendanceDate date NOT NULL,
                  AttendanceYear int NOT NULL,
                  PunchType int NOT NULL,
                  Source int NOT NULL,
                  VerifyMode nvarchar(64) NULL,
                  IsCorrected bit NOT NULL DEFAULT 0,
                  Remarks nvarchar(max) NULL,
                  SyncedAt datetime2 NULL,
                  ArchivedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL
                );
                """, ct);

            await Exec(db, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceLogs_AttendanceDate') CREATE INDEX IX_AttendanceLogs_AttendanceDate ON AttendanceLogs(AttendanceDate);", ct);
            await Exec(db, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceLogs_DeviceId') CREATE INDEX IX_AttendanceLogs_DeviceId ON AttendanceLogs(DeviceId);", ct);
            await Exec(db, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceLogs_Year_Date') CREATE INDEX IX_AttendanceLogs_Year_Date ON AttendanceLogs(AttendanceYear, AttendanceDate);", ct);
            await Exec(db, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceLogs_Employee_Date') CREATE INDEX IX_AttendanceLogs_Employee_Date ON AttendanceLogs(EmployeeId, AttendanceDate);", ct);
            await Exec(db, "IF OBJECT_ID('AttendanceDailySummaries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceDailySummaries_Employee_Date') CREATE UNIQUE INDEX IX_AttendanceDailySummaries_Employee_Date ON AttendanceDailySummaries(EmployeeId, AttendanceDate);", ct);
            await Exec(db, "IF OBJECT_ID('AttendanceDailySummaries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceDailySummaries_Date') CREATE INDEX IX_AttendanceDailySummaries_Date ON AttendanceDailySummaries(AttendanceDate);", ct);
            await Exec(db, "IF OBJECT_ID('AttendanceDailySummaries','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AttendanceDailySummaries_Year_Date') CREATE INDEX IX_AttendanceDailySummaries_Year_Date ON AttendanceDailySummaries(AttendanceYear, AttendanceDate);", ct);

            await Exec(db, """
                UPDATE AttendanceLogs SET AttendanceDate = CAST(PunchTime AS date), AttendanceYear = YEAR(PunchTime)
                WHERE AttendanceDate = '2000-01-01' OR AttendanceYear = 2000;
                """, ct);

            logger.LogInformation("Performance schema bootstrap applied.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Performance schema bootstrap skipped or partially applied.");
        }
    }

    public static async Task ApplyBiometricSchemaAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await Exec(db, """
                IF COL_LENGTH('BiometricTemplates','DeviceId') IS NULL
                  ALTER TABLE BiometricTemplates ADD DeviceId int NULL;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('BiometricTemplates','CapturedOnDeviceSerial') IS NULL
                  ALTER TABLE BiometricTemplates ADD CapturedOnDeviceSerial nvarchar(64) NULL;
                """, ct);

            await Exec(db, """
                IF OBJECT_ID('BiometricEnrollments','U') IS NULL
                CREATE TABLE BiometricEnrollments (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  EmployeeId int NOT NULL,
                  DeviceId int NOT NULL,
                  Type int NOT NULL,
                  FingerIndex int NOT NULL DEFAULT 0,
                  Status int NOT NULL DEFAULT 1,
                  RequestedByUserName nvarchar(128) NULL,
                  StartedAt datetime2 NULL,
                  CompletedAt datetime2 NULL,
                  ExpiresAt datetime2 NOT NULL,
                  ErrorMessage nvarchar(max) NULL,
                  ResultTemplateId int NULL,
                  DispatchedToGateway bit NOT NULL DEFAULT 0,
                  DeviceCommand nvarchar(max) NULL,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL,
                  CONSTRAINT FK_BiometricEnrollments_Employees FOREIGN KEY (EmployeeId) REFERENCES Employees(Id),
                  CONSTRAINT FK_BiometricEnrollments_Devices FOREIGN KEY (DeviceId) REFERENCES BiometricDevices(Id)
                );
                """, ct);

            await Exec(db, "IF OBJECT_ID('BiometricEnrollments','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BiometricEnrollments_Employee_Status') CREATE INDEX IX_BiometricEnrollments_Employee_Status ON BiometricEnrollments(EmployeeId, Status);", ct);
            await Exec(db, "IF OBJECT_ID('BiometricTemplates','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BiometricTemplates_Employee_Type_Finger') CREATE INDEX IX_BiometricTemplates_Employee_Type_Finger ON BiometricTemplates(EmployeeId, Type, FingerIndex);", ct);

            await Exec(db, """
                IF COL_LENGTH('PayrollCutoffs','VpApprovedAt') IS NULL
                  ALTER TABLE PayrollCutoffs ADD VpApprovedAt datetime2 NULL;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('PayrollCutoffs','VpApprovedByName') IS NULL
                  ALTER TABLE PayrollCutoffs ADD VpApprovedByName nvarchar(128) NULL;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('PagIbigConfigs','EmployeeLowRatePercent') IS NULL
                  ALTER TABLE PagIbigConfigs ADD EmployeeLowRatePercent decimal(18,2) NOT NULL CONSTRAINT DF_PagIbigConfigs_LowRate DEFAULT 1;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('PagIbigConfigs','EmployeeLowThreshold') IS NULL
                  ALTER TABLE PagIbigConfigs ADD EmployeeLowThreshold decimal(18,2) NOT NULL CONSTRAINT DF_PagIbigConfigs_LowThreshold DEFAULT 1500;
                """, ct);

            logger.LogInformation("Biometric enrollment schema bootstrap applied.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Biometric schema bootstrap skipped or partially applied.");
        }
    }

    /// <summary>Aligns leave types with company policy: Emergency Leave (10) + SIL (5) only.</summary>
    public static async Task ApplyLeavePolicyAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await Exec(db, """
                IF COL_LENGTH('LeaveTypes','IsConvertibleToCash') IS NULL
                  ALTER TABLE LeaveTypes ADD IsConvertibleToCash bit NOT NULL CONSTRAINT DF_LeaveTypes_Cash DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('LeaveTypes','RegularEmployeesOnly') IS NULL
                  ALTER TABLE LeaveTypes ADD RegularEmployeesOnly bit NOT NULL CONSTRAINT DF_LeaveTypes_RegularOnly DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('LeaveRequests','IsUndertime') IS NULL
                  ALTER TABLE LeaveRequests ADD IsUndertime bit NOT NULL CONSTRAINT DF_LeaveReq_Undertime DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('LeaveRequests','UndertimeHours') IS NULL
                  ALTER TABLE LeaveRequests ADD UndertimeHours decimal(18,2) NOT NULL CONSTRAINT DF_LeaveReq_UtHours DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Payslips','UndertimeElDays') IS NULL
                  ALTER TABLE Payslips ADD UndertimeElDays decimal(18,4) NOT NULL CONSTRAINT DF_Payslips_UtElDays DEFAULT 0;
                """, ct);

            // Retire legacy leave types (VL, SL, LWOP, etc.).
            await db.LeaveTypes
                .Where(t => t.Code == "VL" || t.Code == "SL" || t.Code == "LWOP" || t.Category == LeaveCategory.Vacation || t.Category == LeaveCategory.Sick || t.Category == LeaveCategory.Unpaid)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsActive, false), ct);

            var el = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Code == "EL", ct);
            if (el is null)
            {
                el = new LeaveType { Code = "EL", Name = "Emergency Leave", Category = LeaveCategory.Emergency, DefaultAnnualCredits = 10 };
                db.LeaveTypes.Add(el);
            }
            else
            {
                el.Name = "Emergency Leave";
                el.Category = LeaveCategory.Emergency;
                el.DefaultAnnualCredits = 10;
                el.IsActive = true;
                el.IsPaid = true;
            }

            var sil = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Code == "SIL", ct);
            if (sil is null)
            {
                sil = new LeaveType { Code = "SIL", Name = "Service Incentive Leave", Category = LeaveCategory.ServiceIncentive, DefaultAnnualCredits = 5, RequiresCeoApproval = true, IsConvertibleToCash = true, RegularEmployeesOnly = true };
                db.LeaveTypes.Add(sil);
            }
            else
            {
                sil.Name = "Service Incentive Leave";
                sil.Category = LeaveCategory.ServiceIncentive;
                sil.DefaultAnnualCredits = 5;
                sil.IsActive = true;
                sil.IsPaid = true;
                sil.IsConvertibleToCash = true;
                sil.RegularEmployeesOnly = true;
                sil.RequiresCeoApproval = true;
            }

            await db.SaveChangesAsync(ct);

            var year = DateTime.UtcNow.Year;

            // Normalize Emergency Leave entitlements to 10 days for the current year.
            await db.LeaveBalances
                .Where(b => b.LeaveTypeId == el.Id && b.Year == year && b.Credits < el.DefaultAnnualCredits)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Credits, el.DefaultAnnualCredits), ct);

            var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);

            var employees = await db.Employees
                .Where(e => e.Status != EmploymentStatus.Resigned && e.Status != EmploymentStatus.Terminated && e.Status != EmploymentStatus.Retired)
                .Where(e => !execIds.Contains(e.Id))
                .ToListAsync(ct);
            foreach (var emp in employees)
            {
                var hasEl = await db.LeaveBalances.AnyAsync(b => b.EmployeeId == emp.Id && b.LeaveTypeId == el.Id && b.Year == year, ct);
                if (!hasEl)
                    db.LeaveBalances.Add(new LeaveBalance { EmployeeId = emp.Id, LeaveTypeId = el.Id, Year = year, Credits = el.DefaultAnnualCredits });

                if (emp.Status == EmploymentStatus.Regular)
                {
                    var hasSil = await db.LeaveBalances.AnyAsync(b => b.EmployeeId == emp.Id && b.LeaveTypeId == sil.Id && b.Year == year, ct);
                    if (!hasSil)
                        db.LeaveBalances.Add(new LeaveBalance { EmployeeId = emp.Id, LeaveTypeId = sil.Id, Year = year, Credits = sil.DefaultAnnualCredits });
                }
            }
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Leave policy bootstrap applied (EL 10 days, SIL 5 days).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Leave policy bootstrap skipped or partially applied.");
        }
    }

    /// <summary>Removes attendance/payroll records for VP &amp; CEO (approval-only owners).</summary>
    public static async Task ApplyExecutiveExemptionAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var execIds = await ExecutiveExemption.GetExemptEmployeeIdsAsync(db);
            if (execIds.Count == 0) return;

            await db.AttendanceLogs.Where(a => execIds.Contains(a.EmployeeId)).ExecuteDeleteAsync(ct);
            await db.AttendanceDailySummaries.Where(s => execIds.Contains(s.EmployeeId)).ExecuteDeleteAsync(ct);
            await db.AttendanceCorrections.Where(c => execIds.Contains(c.EmployeeId)).ExecuteDeleteAsync(ct);
            await db.Payslips.Where(p => execIds.Contains(p.EmployeeId)).ExecuteDeleteAsync(ct);

            logger.LogInformation("Executive exemption cleanup applied for {Count} owner account(s).", execIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Executive exemption cleanup skipped or partially applied.");
        }
    }

    public static async Task ApplyApprovalSchemaAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await Exec(db, """
                IF COL_LENGTH('ApprovalActions','HiddenFromHistory') IS NULL
                  ALTER TABLE ApprovalActions ADD HiddenFromHistory bit NOT NULL CONSTRAINT DF_ApprovalActions_Hidden DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('WorkflowTemplateSteps','IsApplyOnlyStep') IS NULL
                  ALTER TABLE WorkflowTemplateSteps ADD IsApplyOnlyStep bit NOT NULL CONSTRAINT DF_WTS_ApplyOnly DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('AttendanceCorrections','IssueType') IS NULL
                  ALTER TABLE AttendanceCorrections ADD IssueType int NOT NULL CONSTRAINT DF_AC_Issue DEFAULT 99;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('AttendanceCorrections','SupportingDocument') IS NULL
                  ALTER TABLE AttendanceCorrections ADD SupportingDocument nvarchar(512) NULL;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('AttendanceCorrections','CurrentApprovalLevel') IS NULL
                  ALTER TABLE AttendanceCorrections ADD CurrentApprovalLevel int NOT NULL CONSTRAINT DF_AC_Level DEFAULT 1;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('AttendanceCorrections','PayrollAppliedAt') IS NULL
                  ALTER TABLE AttendanceCorrections ADD PayrollAppliedAt datetime2 NULL;
                """, ct);
            await Exec(db, """
                IF OBJECT_ID('OvertimeCorrections','U') IS NULL
                CREATE TABLE OvertimeCorrections (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  EmployeeId int NOT NULL,
                  OvertimeDate date NOT NULL,
                  StartTime time NOT NULL,
                  EndTime time NOT NULL,
                  Hours decimal(8,2) NOT NULL,
                  IssueType int NOT NULL DEFAULT 99,
                  Reason nvarchar(512) NOT NULL,
                  SupportingDocument nvarchar(512) NULL,
                  Status int NOT NULL DEFAULT 1,
                  CurrentApprovalLevel int NOT NULL DEFAULT 1,
                  RequestedByUserId int NOT NULL,
                  PayrollAppliedAt datetime2 NULL,
                  CreatedOvertimeRequestId int NULL,
                  ApproverRemarks nvarchar(512) NULL,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL,
                  CONSTRAINT FK_OvertimeCorrections_Employees FOREIGN KEY (EmployeeId) REFERENCES Employees(Id)
                );
                """, ct);
            await EnsureWorkflowTemplatesAsync(db, ct);
            logger.LogInformation("Approval schema bootstrap applied.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Approval schema bootstrap skipped or partially applied.");
        }
    }

    public static async Task ApplySystemSettingsSchemaAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await Exec(db, """
                IF OBJECT_ID('SystemSettings','U') IS NULL
                CREATE TABLE SystemSettings (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  [Key] nvarchar(128) NOT NULL,
                  Value nvarchar(512) NOT NULL,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL
                );
                """, ct);
            await Exec(db, """
                IF OBJECT_ID('SystemSettings','U') IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SystemSettings_Key')
                  CREATE UNIQUE INDEX IX_SystemSettings_Key ON SystemSettings([Key]);
                """, ct);

            if (!await db.SystemSettings.AnyAsync(s => s.Key == "SyncBatchRetentionDays", ct))
            {
                db.SystemSettings.Add(new SystemSetting { Key = "SyncBatchRetentionDays", Value = "30" });
                await db.SaveChangesAsync(ct);
            }

            logger.LogInformation("System settings schema bootstrap applied.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "System settings schema bootstrap skipped or partially applied.");
        }
    }

    public static async Task ApplyEmployeeStatutorySchemaAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await Exec(db, """
                IF COL_LENGTH('Employees','UseManualStatutoryContributions') IS NULL
                  ALTER TABLE Employees ADD UseManualStatutoryContributions bit NOT NULL CONSTRAINT DF_Employees_ManualStatutory DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Employees','ManualSssEmployee') IS NULL
                  ALTER TABLE Employees ADD ManualSssEmployee decimal(18,2) NOT NULL CONSTRAINT DF_Employees_ManualSssEe DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Employees','ManualSssEmployer') IS NULL
                  ALTER TABLE Employees ADD ManualSssEmployer decimal(18,2) NOT NULL CONSTRAINT DF_Employees_ManualSssEr DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Employees','ManualPhilHealthEmployee') IS NULL
                  ALTER TABLE Employees ADD ManualPhilHealthEmployee decimal(18,2) NOT NULL CONSTRAINT DF_Employees_ManualPhEe DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Employees','ManualPhilHealthEmployer') IS NULL
                  ALTER TABLE Employees ADD ManualPhilHealthEmployer decimal(18,2) NOT NULL CONSTRAINT DF_Employees_ManualPhEr DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Employees','ManualPagIbigEmployee') IS NULL
                  ALTER TABLE Employees ADD ManualPagIbigEmployee decimal(18,2) NOT NULL CONSTRAINT DF_Employees_ManualPiEe DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Employees','ManualPagIbigEmployer') IS NULL
                  ALTER TABLE Employees ADD ManualPagIbigEmployer decimal(18,2) NOT NULL CONSTRAINT DF_Employees_ManualPiEr DEFAULT 0;
                """, ct);
            await Exec(db, """
                IF COL_LENGTH('Employees','ManualWithholdingTax') IS NULL
                  ALTER TABLE Employees ADD ManualWithholdingTax decimal(18,2) NOT NULL CONSTRAINT DF_Employees_ManualTax DEFAULT 0;
                """, ct);
            logger.LogInformation("Employee statutory schema bootstrap applied.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Employee statutory schema bootstrap skipped or partially applied.");
        }
    }

    public static async Task ApplyPayrollDeductionSchemaAsync(HrisDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await Exec(db, """
                IF OBJECT_ID('DeductionTypes','U') IS NULL
                CREATE TABLE DeductionTypes (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  Code nvarchar(32) NOT NULL,
                  Name nvarchar(128) NOT NULL,
                  ApplicableHalf int NOT NULL DEFAULT 0,
                  SortOrder int NOT NULL DEFAULT 0,
                  IsActive bit NOT NULL DEFAULT 1,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL
                );
                """, ct);
            await Exec(db, """
                IF OBJECT_ID('EmployeeDeductions','U') IS NULL
                CREATE TABLE EmployeeDeductions (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  EmployeeId int NOT NULL,
                  DeductionTypeId int NOT NULL,
                  LoanId int NULL,
                  Amount decimal(18,2) NOT NULL DEFAULT 0,
                  RemainingBalance decimal(18,2) NULL,
                  TotalInstallments int NULL,
                  PaidInstallments int NOT NULL DEFAULT 0,
                  Frequency int NOT NULL DEFAULT 1,
                  IsProfileEnabled bit NOT NULL DEFAULT 1,
                  IsActive bit NOT NULL DEFAULT 1,
                  EffectiveFrom date NULL,
                  EffectiveTo date NULL,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL,
                  CONSTRAINT FK_EmployeeDeductions_Employees FOREIGN KEY (EmployeeId) REFERENCES Employees(Id),
                  CONSTRAINT FK_EmployeeDeductions_DeductionTypes FOREIGN KEY (DeductionTypeId) REFERENCES DeductionTypes(Id),
                  CONSTRAINT FK_EmployeeDeductions_Loans FOREIGN KEY (LoanId) REFERENCES Loans(Id)
                );
                """, ct);
            await Exec(db, """
                IF OBJECT_ID('PayrollCutoffDeductionSelections','U') IS NULL
                CREATE TABLE PayrollCutoffDeductionSelections (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  PayrollCutoffId int NOT NULL,
                  EmployeeId int NOT NULL,
                  EmployeeDeductionId int NOT NULL,
                  IsApplied bit NOT NULL DEFAULT 1,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL,
                  CONSTRAINT FK_PCDS_Cutoffs FOREIGN KEY (PayrollCutoffId) REFERENCES PayrollCutoffs(Id) ON DELETE CASCADE,
                  CONSTRAINT FK_PCDS_Employees FOREIGN KEY (EmployeeId) REFERENCES Employees(Id),
                  CONSTRAINT FK_PCDS_EmployeeDeductions FOREIGN KEY (EmployeeDeductionId) REFERENCES EmployeeDeductions(Id)
                );
                """, ct);
            await Exec(db, """
                IF OBJECT_ID('DeductionTemplates','U') IS NULL
                CREATE TABLE DeductionTemplates (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  Name nvarchar(128) NOT NULL,
                  Description nvarchar(512) NULL,
                  IsActive bit NOT NULL DEFAULT 1,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL
                );
                """, ct);
            await Exec(db, """
                IF OBJECT_ID('DeductionTemplateItems','U') IS NULL
                CREATE TABLE DeductionTemplateItems (
                  Id int IDENTITY(1,1) PRIMARY KEY,
                  TemplateId int NOT NULL,
                  DeductionTypeId int NOT NULL,
                  CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                  UpdatedAt datetime2 NULL,
                  CONSTRAINT FK_DTI_Templates FOREIGN KEY (TemplateId) REFERENCES DeductionTemplates(Id) ON DELETE CASCADE,
                  CONSTRAINT FK_DTI_Types FOREIGN KEY (DeductionTypeId) REFERENCES DeductionTypes(Id)
                );
                """, ct);
            await Exec(db, "IF OBJECT_ID('DeductionTypes','U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeductionTypes_Code') CREATE UNIQUE INDEX IX_DeductionTypes_Code ON DeductionTypes(Code);", ct);
            await Exec(db, """
                IF COL_LENGTH('PayrollCutoffs','DeductOtherDeductions') IS NULL
                  ALTER TABLE PayrollCutoffs ADD DeductOtherDeductions bit NOT NULL CONSTRAINT DF_PayrollCutoffs_OtherDed DEFAULT 1;
                """, ct);
            logger.LogInformation("Payroll deduction schema bootstrap applied.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Payroll deduction schema bootstrap skipped or partially applied.");
        }
    }

    private static async Task EnsureWorkflowTemplatesAsync(HrisDbContext db, CancellationToken ct)
    {
        await UpsertWorkflowStep(db, RequestType.AttendanceCorrection, 1, UserRole.DepartmentHead, "Department Head / Supervisor", false, ct);
        await UpsertWorkflowStep(db, RequestType.AttendanceCorrection, 2, UserRole.HrOfficer, "HR Officer", false, ct);
        await UpsertWorkflowStep(db, RequestType.AttendanceCorrection, 3, UserRole.PayrollOfficer, "Payroll Officer", true, ct);
        await UpsertWorkflowStep(db, RequestType.OvertimeCorrection, 1, UserRole.DepartmentHead, "Department Head", false, ct);
        await UpsertWorkflowStep(db, RequestType.OvertimeCorrection, 2, UserRole.HrOfficer, "HR Officer", false, ct);
        await UpsertWorkflowStep(db, RequestType.OvertimeCorrection, 3, UserRole.PayrollOfficer, "Payroll Officer", true, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertWorkflowStep(HrisDbContext db, RequestType type, int level, UserRole role, string name, bool applyOnly, CancellationToken ct)
    {
        var step = await db.WorkflowTemplateSteps.FirstOrDefaultAsync(s => s.RequestType == type && s.Level == level, ct);
        if (step is null)
        {
            db.WorkflowTemplateSteps.Add(new WorkflowTemplateStep
            {
                RequestType = type, Level = level, ApproverRole = role, StepName = name, IsApplyOnlyStep = applyOnly
            });
        }
        else
        {
            step.ApproverRole = role;
            step.StepName = name;
            step.IsApplyOnlyStep = applyOnly;
        }
    }

    private static Task Exec(HrisDbContext db, string sql, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(sql, ct);
}
