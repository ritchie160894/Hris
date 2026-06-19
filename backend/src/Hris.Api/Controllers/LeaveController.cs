using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

[ApiController]
[Route("api/leave")]
[Authorize]
public class LeaveController(HrisDbContext db, ApprovalService approvals, NotificationService notifications, AuditService audit) : ControllerBase
{
    [HttpGet("types")]
    public async Task<IActionResult> Types() => Ok(await db.LeaveTypes
        .Where(t => t.IsActive && (t.Category == LeaveCategory.Emergency || t.Category == LeaveCategory.ServiceIncentive))
        .ToListAsync());

    [HttpGet("balances")]
    public async Task<IActionResult> Balances([FromQuery] int? employeeId, [FromQuery] int? year, [FromQuery] bool mine = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        int? eid = null;

        if (mine)
        {
            eid = User.EmployeeId();
            if (!eid.HasValue)
            {
                pageSize = Math.Clamp(pageSize, 1, 100);
                return Ok(new { total = 0, page, pageSize, items = Array.Empty<object>() });
            }
        }
        else if (User.Role() == UserRole.Employee)
        {
            eid = User.EmployeeId();
        }
        else if (employeeId.HasValue)
        {
            if (!User.IsHr() && User.EmployeeId() != employeeId)
                return Forbid();
            eid = employeeId;
        }
        // HR roles without mine/employeeId: company-wide list (Leave Management module only).

        var y = year ?? DateTime.Today.Year;
        var q = db.LeaveBalances.Include(b => b.LeaveType).Include(b => b.Employee)
            .Where(b => b.Year == y && (b.LeaveType!.Category == LeaveCategory.Emergency || b.LeaveType.Category == LeaveCategory.ServiceIncentive));
        if (eid.HasValue) q = q.Where(b => b.EmployeeId == eid);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.OrderBy(b => b.Employee!.LastName).ThenBy(b => b.Employee.FirstName).ThenBy(b => b.LeaveType!.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new
            {
                b.Id, b.Year, b.Credits, b.Used, remaining = b.Credits - b.Used,
                leaveType = new { b.LeaveType!.Id, b.LeaveType.Code, b.LeaveType.Name },
                employee = new { b.Employee!.Id, b.Employee.EmployeeCode, name = b.Employee.FirstName + " " + b.Employee.LastName }
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Self-service: current user's leave credits only (never company-wide).</summary>
    [HttpGet("my-balances")]
    public Task<IActionResult> MyBalances([FromQuery] int? year, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
        => Balances(null, year, mine: true, page, pageSize);

    [HttpPut("balances/{id:int}")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.HrOfficer)}")]
    public async Task<IActionResult> AdjustBalance(int id, [FromBody] BalanceAdjust req)
    {
        var b = await db.LeaveBalances.FindAsync(id);
        if (b is null) return NotFound();
        b.Credits = req.Credits;
        audit.Log(AuditCategory.RecordChange, $"Adjusted leave balance #{id} to {req.Credits}", nameof(LeaveBalance), id.ToString());
        await db.SaveChangesAsync();
        return Ok(b);
    }
    public record BalanceAdjust(decimal Credits);

    [HttpGet("requests")]
    public async Task<IActionResult> Requests([FromQuery] string? status, [FromQuery] int? employeeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = db.LeaveRequests.Include(l => l.Employee).ThenInclude(e => e!.Department).Include(l => l.LeaveType).AsQueryable();
        if (User.Role() == UserRole.Employee) q = q.Where(l => l.EmployeeId == User.EmployeeId());
        else if (User.Role() == UserRole.DepartmentHead)
        {
            var me = await db.Users.Include(u => u.Employee).FirstAsync(u => u.Id == User.UserId());
            q = q.Where(l => l.Employee!.DepartmentId == me.Employee!.DepartmentId);
        }
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, out var st)) q = q.Where(l => l.Status == st);
        if (employeeId.HasValue) q = q.Where(l => l.EmployeeId == employeeId);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.Id, l.StartDate, l.EndDate, l.Days, l.IsHalfDay, l.IsUndertime, l.UndertimeHours, l.Reason,
                status = l.Status.ToString(), l.CurrentApprovalLevel, l.CreatedAt,
                leaveType = new { l.LeaveType!.Id, l.LeaveType.Code, l.LeaveType.Name, isSil = l.LeaveType.Category == LeaveCategory.ServiceIncentive },
                employee = new { l.Employee!.Id, l.Employee.EmployeeCode, name = l.Employee.FirstName + " " + l.Employee.LastName, department = l.Employee.Department!.Name }
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("requests/{id:int}")]
    public async Task<IActionResult> GetRequest(int id)
    {
        var l = await db.LeaveRequests.Include(x => x.Employee).Include(x => x.LeaveType).FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return NotFound();
        var isSil = l.LeaveType!.Category == LeaveCategory.ServiceIncentive;
        var chain = await db.ApprovalActions
            .Where(a => a.RequestType == (isSil ? RequestType.ServiceIncentiveLeave : RequestType.Leave) && a.RequestId == id)
            .OrderBy(a => a.Level).ToListAsync();
        return Ok(new { request = l, approvalChain = chain });
    }

    [HttpPost("requests")]
    public async Task<IActionResult> Submit(LeaveRequest input)
    {
        var eid = User.EmployeeId();
        if (User.Role() == UserRole.Employee)
        {
            if (eid is null) return BadRequest(new { message = "No employee profile linked." });
            input.EmployeeId = eid.Value;
        }
        else if (input.EmployeeId <= 0 && eid is int linked)
            input.EmployeeId = linked;

        if (input.EmployeeId <= 0) return BadRequest(new { message = "Employee is required." });

        var leaveType = await db.LeaveTypes.FindAsync(input.LeaveTypeId);
        if (leaveType is null) return BadRequest(new { message = "Invalid leave type." });

        // SIL policy: only Regular employees may avail.
        if (leaveType.RegularEmployeesOnly)
        {
            var requester = await db.Employees.FindAsync(input.EmployeeId);
            if (requester is null || requester.Status != EmploymentStatus.Regular)
                return BadRequest(new { message = $"{leaveType.Name} is only available to Regular employees." });
        }

        if (input.IsUndertime)
        {
            if (leaveType.Category is not (LeaveCategory.Emergency or LeaveCategory.ServiceIncentive))
                return BadRequest(new { message = "Undertime must be filed under Emergency Leave (EL) or Service Incentive Leave (SIL)." });
            if (input.UndertimeHours <= 0)
                return BadRequest(new { message = "Undertime hours are required." });
            input.EndDate = input.StartDate;
            input.IsHalfDay = false;
            var requester = await db.Employees.FindAsync(input.EmployeeId);
            if (requester is null) return BadRequest(new { message = "Employee not found." });
            var scheduledHours = ScheduledHoursPerDay(requester);
            if (scheduledHours <= 0) return BadRequest(new { message = "Employee shift is not configured." });
            if (input.UndertimeHours > scheduledHours)
                return BadRequest(new { message = $"Undertime cannot exceed scheduled shift ({scheduledHours:n2} hr/s)." });
            input.Days = Math.Round(input.UndertimeHours / scheduledHours, 4);
        }
        else if (input.Days <= 0)
            input.Days = input.IsHalfDay ? 0.5m : (input.EndDate.DayNumber - input.StartDate.DayNumber + 1);

        if (input.IsHalfDay && input.IsUndertime)
            return BadRequest(new { message = "Choose either half day or undertime, not both." });

        // check balance
        var balance = await db.LeaveBalances.FirstOrDefaultAsync(b =>
            b.EmployeeId == input.EmployeeId && b.LeaveTypeId == input.LeaveTypeId && b.Year == input.StartDate.Year);
        if (leaveType.DefaultAnnualCredits > 0 && (balance is null || balance.Credits - balance.Used < input.Days))
            return BadRequest(new { message = $"Insufficient {leaveType.Name} credits. Remaining: {(balance == null ? 0 : balance.Credits - balance.Used)}" });

        input.Id = 0;
        input.Status = RequestStatus.Pending;
        input.CurrentApprovalLevel = 1;
        input.LeaveType = null; input.Employee = null;
        db.LeaveRequests.Add(input);
        await db.SaveChangesAsync();

        // Workflow routing (full / half / undertime all use the same chain per leave category):
        // EL  → RequestType.Leave                 → Dept Head → HR → VP → CEO
        // SIL → RequestType.ServiceIncentiveLeave → VP → CEO (Regular employees only)
        var requestType = leaveType.Category == LeaveCategory.ServiceIncentive ? RequestType.ServiceIncentiveLeave : RequestType.Leave;
        var summary = input.IsUndertime
            ? $"{leaveType.Code} undertime {input.UndertimeHours:n2} hr/s on {input.StartDate:MMM d} ({input.Days} day/s charged)"
            : $"{leaveType.Name} from {input.StartDate:MMM d} to {input.EndDate:MMM d} ({input.Days} day/s)";
        await approvals.InitializeChainAsync(requestType, input.Id, input.EmployeeId, summary, User.Role());
        await db.SaveChangesAsync();
        return Ok(input);
    }

    private static decimal ScheduledHoursPerDay(Employee emp)
    {
        var span = emp.ShiftEnd.ToTimeSpan() - emp.ShiftStart.ToTimeSpan();
        if (span <= TimeSpan.Zero) span = TimeSpan.FromHours(8);
        return Math.Round(Math.Max(0, (decimal)span.TotalHours - emp.BreakMinutes / 60m), 2);
    }

    [HttpPost("requests/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var l = await db.LeaveRequests.FindAsync(id);
        if (l is null) return NotFound();
        if (User.Role() == UserRole.Employee && l.EmployeeId != User.EmployeeId()) return Forbid();
        if (l.Status is RequestStatus.Approved or RequestStatus.Rejected)
            return BadRequest(new { message = "Completed requests cannot be cancelled." });
        l.Status = RequestStatus.Cancelled;
        await db.SaveChangesAsync();
        return Ok(new { status = l.Status.ToString() });
    }

    /// <summary>Calendar of approved leaves for a month.</summary>
    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar([FromQuery] int year, [FromQuery] int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var leaves = await db.LeaveRequests.Include(l => l.Employee).Include(l => l.LeaveType)
            .Where(l => l.Status == RequestStatus.Approved && l.StartDate <= last && l.EndDate >= first)
            .Select(l => new
            {
                l.Id, l.StartDate, l.EndDate,
                employee = l.Employee!.FirstName + " " + l.Employee.LastName,
                leaveType = l.LeaveType!.Code
            }).ToListAsync();
        var holidays = await db.Holidays.Where(h => h.Date >= first && h.Date <= last).ToListAsync();
        return Ok(new { leaves, holidays });
    }
}
