using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Data;

public class HrisDbContext(DbContextOptions<HrisDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<EmployeeHistory> EmployeeHistories => Set<EmployeeHistory>();
    public DbSet<BiometricTemplate> BiometricTemplates => Set<BiometricTemplate>();
    public DbSet<BiometricEnrollment> BiometricEnrollments => Set<BiometricEnrollment>();

    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<AttendanceDailySummary> AttendanceDailySummaries => Set<AttendanceDailySummary>();
    public DbSet<AttendanceLogArchive> AttendanceLogArchives => Set<AttendanceLogArchive>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<BiometricDevice> BiometricDevices => Set<BiometricDevice>();
    public DbSet<DeviceActivityLog> DeviceActivityLogs => Set<DeviceActivityLog>();

    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<OvertimeCorrection> OvertimeCorrections => Set<OvertimeCorrection>();
    public DbSet<WorkflowTemplateStep> WorkflowTemplateSteps => Set<WorkflowTemplateStep>();
    public DbSet<ApprovalAction> ApprovalActions => Set<ApprovalAction>();

    public DbSet<PayrollCutoff> PayrollCutoffs => Set<PayrollCutoff>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<PayComponent> PayComponents => Set<PayComponent>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanPayment> LoanPayments => Set<LoanPayment>();
    public DbSet<SssBracket> SssBrackets => Set<SssBracket>();
    public DbSet<PhilHealthConfig> PhilHealthConfigs => Set<PhilHealthConfig>();
    public DbSet<PagIbigConfig> PagIbigConfigs => Set<PagIbigConfig>();
    public DbSet<TaxBracket> TaxBrackets => Set<TaxBracket>();
    public DbSet<Benefit> Benefits => Set<Benefit>();
    public DbSet<EmployeeBenefit> EmployeeBenefits => Set<EmployeeBenefit>();

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();
    public DbSet<KpiScore> KpiScores => Set<KpiScore>();
    public DbSet<Training> Trainings => Set<Training>();
    public DbSet<TrainingParticipant> TrainingParticipants => Set<TrainingParticipant>();

    public DbSet<SyncBatch> SyncBatches => Set<SyncBatch>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<DeductionType> DeductionTypes => Set<DeductionType>();
    public DbSet<EmployeeDeduction> EmployeeDeductions => Set<EmployeeDeduction>();
    public DbSet<PayrollCutoffDeductionSelection> PayrollCutoffDeductionSelections => Set<PayrollCutoffDeductionSelection>();
    public DbSet<DeductionTemplate> DeductionTemplates => Set<DeductionTemplate>();
    public DbSet<DeductionTemplateItem> DeductionTemplateItems => Set<DeductionTemplateItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.EmployeeCode).IsUnique();
            e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.MonthlySalary).HasPrecision(18, 2);
            e.Property(x => x.DailyRate).HasPrecision(18, 2);
            e.Ignore(x => x.RowVersionStamp);
        });

        b.Entity<Department>()
            .HasOne(x => x.HeadEmployee).WithMany().HasForeignKey(x => x.HeadEmployeeId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<AttendanceLog>(e =>
        {
            e.HasIndex(x => x.SyncGuid).IsUnique();
            e.HasIndex(x => new { x.EmployeeId, x.PunchTime });
            e.HasIndex(x => new { x.EmployeeId, x.PunchTime, x.PunchType }).IsUnique();
            // Performance indexes (Solutions 1 & 2)
            e.HasIndex(x => x.AttendanceDate).HasDatabaseName("IX_AttendanceLogs_AttendanceDate");
            e.HasIndex(x => x.DeviceId).HasDatabaseName("IX_AttendanceLogs_DeviceId");
            e.HasIndex(x => new { x.AttendanceYear, x.AttendanceDate }).HasDatabaseName("IX_AttendanceLogs_Year_Date");
            e.HasIndex(x => new { x.EmployeeId, x.AttendanceDate }).HasDatabaseName("IX_AttendanceLogs_Employee_Date");
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AttendanceDailySummary>(e =>
        {
            e.HasIndex(x => new { x.EmployeeId, x.AttendanceDate }).IsUnique();
            e.HasIndex(x => x.AttendanceDate).HasDatabaseName("IX_AttendanceDailySummaries_Date");
            e.HasIndex(x => new { x.AttendanceYear, x.AttendanceDate }).HasDatabaseName("IX_AttendanceDailySummaries_Year_Date");
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AttendanceLogArchive>(e =>
        {
            e.HasIndex(x => x.SyncGuid);
            e.HasIndex(x => new { x.AttendanceYear, x.AttendanceDate });
            e.HasIndex(x => new { x.EmployeeId, x.AttendanceDate });
        });

        b.Entity<BiometricDevice>().HasIndex(x => x.SerialNumber).IsUnique();
        b.Entity<BiometricTemplate>().HasIndex(x => new { x.EmployeeId, x.Type, x.FingerIndex });
        b.Entity<BiometricEnrollment>().HasIndex(x => new { x.EmployeeId, x.Status });
        b.Entity<User>().HasIndex(x => x.Username).IsUnique();
        b.Entity<LeaveBalance>().HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.Year }).IsUnique();
        b.Entity<ApprovalAction>().HasIndex(x => new { x.RequestType, x.RequestId });
        b.Entity<SystemSetting>().HasIndex(x => x.Key).IsUnique();
        b.Entity<AuditLog>().HasIndex(x => x.CreatedAt);
        b.Entity<Notification>().HasIndex(x => new { x.UserId, x.IsRead });
        b.Entity<Payslip>().HasIndex(x => new { x.PayrollCutoffId, x.EmployeeId }).IsUnique();
        b.Entity<Payslip>().Property(x => x.UndertimeLeaveDays).HasPrecision(18, 4);
        b.Entity<Payslip>().Property(x => x.UndertimeElDays).HasPrecision(18, 4);
        b.Entity<EmployeeDeduction>().HasIndex(x => new { x.EmployeeId, x.DeductionTypeId, x.LoanId });
        b.Entity<PayrollCutoffDeductionSelection>().HasIndex(x => new { x.PayrollCutoffId, x.EmployeeId, x.EmployeeDeductionId }).IsUnique();

        // decimal precision defaults
        foreach (var property in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            if (property.GetPrecision() is null) property.SetPrecision(18);
            if (property.GetScale() is null) property.SetScale(2);
        }
    }
}
