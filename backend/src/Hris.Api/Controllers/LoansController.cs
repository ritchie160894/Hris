using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Controllers;

[ApiController]
[Route("api/loans")]
[Authorize]
public class LoansController(HrisDbContext db, ApprovalService approvals, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? type, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var q = db.Loans.Include(l => l.Employee).AsQueryable();
        if (User.Role() == UserRole.Employee) q = q.Where(l => l.EmployeeId == User.EmployeeId());
        if (!string.IsNullOrEmpty(type) && Enum.TryParse<LoanType>(type, out var lt)) q = q.Where(l => l.Type == lt);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<LoanStatus>(status, out var ls)) q = q.Where(l => l.Status == ls);

        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.Id, type = l.Type.ToString(), l.Reference, l.Principal, l.Balance, l.AmortizationPerCutoff,
                l.StartDate, status = l.Status.ToString(), approvalStatus = l.ApprovalStatus.ToString(), l.Purpose, l.CreatedAt,
                employee = new { l.Employee!.Id, l.Employee.EmployeeCode, name = l.Employee.FirstName + " " + l.Employee.LastName }
            }).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var l = await db.Loans.Include(x => x.Employee).Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return NotFound();
        if (User.Role() == UserRole.Employee && l.EmployeeId != User.EmployeeId()) return Forbid();
        var chain = await db.ApprovalActions
            .Where(a => (a.RequestType == RequestType.Loan || a.RequestType == RequestType.CashAdvance) && a.RequestId == id)
            .OrderBy(a => a.Level).ToListAsync();
        return Ok(new { loan = l, approvalChain = chain });
    }

    /// <summary>Submit loan or cash advance application (goes through the approval workflow).</summary>
    [HttpPost]
    public async Task<IActionResult> Submit(Loan input)
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
        if (input.Principal <= 0) return BadRequest(new { message = "Amount must be greater than zero." });
        if (input.AmortizationPerCutoff <= 0)
            input.AmortizationPerCutoff = Math.Round(input.Principal / 12, 2); // default: 12 cutoffs (~6 months)

        input.Id = 0;
        input.Employee = null;
        input.Status = LoanStatus.PendingApproval;
        input.ApprovalStatus = RequestStatus.Pending;
        input.Balance = input.Principal;
        input.Reference = $"{(input.Type == LoanType.CashAdvance ? "CA" : "LN")}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        db.Loans.Add(input);
        await db.SaveChangesAsync();

        var requestType = input.Type == LoanType.CashAdvance ? RequestType.CashAdvance : RequestType.Loan;
        await approvals.InitializeChainAsync(requestType, input.Id, input.EmployeeId,
            $"{(input.Type == LoanType.CashAdvance ? "Cash advance" : "Loan")} of ₱{input.Principal:n2}", User.Role());
        await db.SaveChangesAsync();
        return Ok(input);
    }

    /// <summary>HR-managed government loans (already approved externally, e.g. SSS loan) added directly.</summary>
    [HttpPost("direct")]
    [Authorize(Roles = $"{nameof(UserRole.SuperAdministrator)},{nameof(UserRole.HrAdministrator)},{nameof(UserRole.PayrollOfficer)}")]
    public async Task<IActionResult> AddDirect(Loan input)
    {
        input.Id = 0;
        input.Employee = null;
        input.Status = LoanStatus.Active;
        input.ApprovalStatus = RequestStatus.Approved;
        input.Balance = input.Principal;
        if (string.IsNullOrEmpty(input.Reference))
            input.Reference = $"GL-{DateTime.UtcNow:yyyyMMddHHmmss}";
        db.Loans.Add(input);
        audit.Log(AuditCategory.RecordChange, $"Added {input.Type} for employee #{input.EmployeeId} (₱{input.Principal:n2})", nameof(Loan));
        await db.SaveChangesAsync();
        return Ok(input);
    }

    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> Payments(int id) =>
        Ok(await db.LoanPayments.Where(p => p.LoanId == id).OrderByDescending(p => p.PaymentDate).ToListAsync());

    /// <summary>Applicant cancels a pending loan / cash advance application.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var loan = await db.Loans.FindAsync(id);
        if (loan is null) return NotFound();

        var eid = User.EmployeeId();
        if (eid is null || loan.EmployeeId != eid)
            return Forbid();

        if (loan.Status != LoanStatus.PendingApproval)
            return BadRequest(new { message = "Only pending applications can be cancelled." });

        if (loan.ApprovalStatus is RequestStatus.Approved or RequestStatus.Rejected or RequestStatus.Cancelled)
            return BadRequest(new { message = "This application can no longer be cancelled." });

        loan.Status = LoanStatus.Cancelled;
        loan.ApprovalStatus = RequestStatus.Cancelled;

        if (loan.Type is LoanType.CashAdvance or LoanType.CompanyLoan)
        {
            var requestType = loan.Type == LoanType.CashAdvance ? RequestType.CashAdvance : RequestType.Loan;
            var steps = await db.ApprovalActions
                .Where(a => a.RequestType == requestType && a.RequestId == id)
                .ToListAsync();
            foreach (var step in steps.Where(s => s.Status is ApprovalStepStatus.Pending or ApprovalStepStatus.Waiting))
                step.Status = ApprovalStepStatus.Skipped;
        }

        audit.Log(AuditCategory.Approval, $"Cancelled {loan.Type} application #{id} ({loan.Reference})", nameof(Loan), id.ToString());
        await db.SaveChangesAsync();
        return Ok(new { status = loan.Status.ToString(), approvalStatus = loan.ApprovalStatus.ToString() });
    }
}
