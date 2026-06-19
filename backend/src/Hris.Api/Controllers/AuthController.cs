using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Hris.Api.Data;
using Hris.Api.Services;
using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Hris.Api.Controllers;

public static class ClaimsExtensions
{
    public static int UserId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue("uid")!);
    public static int? EmployeeId(this ClaimsPrincipal user)
    {
        var v = user.FindFirstValue("eid");
        return string.IsNullOrEmpty(v) ? null : int.Parse(v);
    }
    public static UserRole Role(this ClaimsPrincipal user) => Enum.Parse<UserRole>(user.FindFirstValue(ClaimTypes.Role)!);
    public static string DisplayName(this ClaimsPrincipal user) => user.FindFirstValue("name") ?? user.Identity?.Name ?? "";
    public static bool IsHr(this ClaimsPrincipal user) => user.Role() is UserRole.SuperAdministrator or UserRole.HrAdministrator or UserRole.HrOfficer or UserRole.VicePresidentHrHead;
}

[ApiController]
[Route("api/auth")]
public class AuthController(HrisDbContext db, IConfiguration config, AuditService audit) : ControllerBase
{
    public record LoginRequest(string Username, string Password);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await db.Users.Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Username == req.Username);

        if (user is null || !user.IsActive || user.IsLocked || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= 5) user.IsLocked = true;
            }
            audit.Log(AuditCategory.Login, $"Failed login attempt for '{req.Username}'");
            await db.SaveChangesAsync();
            return Unauthorized(new { message = "Invalid username or password." });
        }

        user.FailedLoginAttempts = 0;
        user.LastLoginAt = DateTime.UtcNow;
        audit.Log(AuditCategory.Login, $"User '{user.Username}' logged in");
        await db.SaveChangesAsync();

        var token = CreateToken(user);
        return Ok(new
        {
            token,
            user = new
            {
                user.Id, user.Username, user.DisplayName, role = user.Role.ToString(),
                user.EmployeeId,
                employeeName = user.Employee?.FullName,
                departmentId = user.Employee?.DepartmentId
            }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await db.Users.Include(u => u.Employee).ThenInclude(e => e!.Department)
            .Include(u => u.Employee).ThenInclude(e => e!.Position)
            .FirstOrDefaultAsync(u => u.Id == User.UserId());
        if (user is null) return NotFound();
        return Ok(new
        {
            user.Id, user.Username, user.DisplayName, role = user.Role.ToString(), user.EmployeeId,
            employee = user.Employee == null ? null : new
            {
                user.Employee.Id, user.Employee.EmployeeCode, user.Employee.FullName,
                department = user.Employee.Department?.Name, position = user.Employee.Position?.Title,
                user.Employee.PhotoUrl
            }
        });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await db.Users.FindAsync(User.UserId());
        if (user is null) return NotFound();
        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Current password is incorrect." });
        if (req.NewPassword.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        audit.Log(AuditCategory.Security, "Password changed");
        await db.SaveChangesAsync();
        return Ok(new { message = "Password updated." });
    }

    private string CreateToken(User user)
    {
        var claims = new List<Claim>
        {
            new("uid", user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("name", user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (user.EmployeeId.HasValue) claims.Add(new Claim("eid", user.EmployeeId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(double.Parse(config["Jwt:ExpiryMinutes"] ?? "480")),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
