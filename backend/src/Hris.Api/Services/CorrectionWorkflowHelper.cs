using Hris.Api.Data;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

public static class CorrectionWorkflowHelper
{
    public static async Task ApplyAttendanceCorrectionAsync(HrisDbContext db, AttendanceCorrection c, CancellationToken ct = default)
    {
        var dayStart = c.AttendanceDate.ToDateTime(TimeOnly.MinValue);
        var dayEnd = c.AttendanceDate.ToDateTime(TimeOnly.MaxValue);
        var log = await db.AttendanceLogs.FirstOrDefaultAsync(a =>
            a.EmployeeId == c.EmployeeId && a.PunchType == c.PunchType &&
            a.PunchTime >= dayStart && a.PunchTime <= dayEnd, ct);
        if (log is not null)
        {
            log.PunchTime = c.CorrectedTime;
            log.IsCorrected = true;
            log.Remarks = $"Corrected: {c.Reason}";
        }
        else
        {
            var newLog = new AttendanceLog
            {
                EmployeeId = c.EmployeeId,
                PunchTime = c.CorrectedTime,
                PunchType = c.PunchType,
                Source = AttendanceSource.Manual,
                IsCorrected = true,
                Remarks = $"Correction: {c.Reason}"
            };
            AttendanceSummaryService.StampLog(newLog);
            db.AttendanceLogs.Add(newLog);
        }
    }

    public static async Task<OvertimeRequest> CreateApprovedOvertimeFromCorrectionAsync(
        HrisDbContext db, OvertimeCorrection c, CancellationToken ct = default)
    {
        var emp = await db.Employees.FindAsync([c.EmployeeId], ct);
        var hourly = emp is null ? 0 : (emp.DailyRate ?? Math.Round(emp.MonthlySalary / 24m, 2)) / 8;
        var ot = new OvertimeRequest
        {
            EmployeeId = c.EmployeeId,
            OvertimeDate = c.OvertimeDate,
            StartTime = c.StartTime,
            EndTime = c.EndTime,
            Hours = c.Hours,
            Reason = $"[Correction] {c.Reason}",
            Status = RequestStatus.Approved,
            CurrentApprovalLevel = 3,
            ComputedPay = Math.Round(c.Hours * hourly * 1.25m, 2),
            CompletedAt = DateTime.UtcNow
        };
        db.OvertimeRequests.Add(ot);
        await db.SaveChangesAsync(ct);
        return ot;
    }

    public static bool IsHrFinalApprovalStep(RequestType type, int completedLevel) =>
        type is RequestType.AttendanceCorrection or RequestType.OvertimeCorrection && completedLevel == 2;
}
