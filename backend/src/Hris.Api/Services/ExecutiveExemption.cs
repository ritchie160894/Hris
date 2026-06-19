using Hris.Api.Data;
using Hris.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Services;

/// <summary>
/// Executives (VP &amp; HR Head, President &amp; CEO) are company owners: their portal is
/// approval-only, so they are exempt from biometric timekeeping and payroll computation.
/// </summary>
public static class ExecutiveExemption
{
    public static Task<List<int>> GetExemptEmployeeIdsAsync(HrisDbContext db) =>
        db.Users
            .Where(u => u.EmployeeId != null &&
                (u.Role == UserRole.VicePresidentHrHead || u.Role == UserRole.PresidentCeo))
            .Select(u => u.EmployeeId!.Value)
            .Distinct()
            .ToListAsync();

    public static Task<bool> IsExemptAsync(HrisDbContext db, int employeeId) =>
        db.Users.AnyAsync(u => u.EmployeeId == employeeId &&
            (u.Role == UserRole.VicePresidentHrHead || u.Role == UserRole.PresidentCeo));
}
