namespace Hris.Domain.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Tin { get; set; }
    public string? Address { get; set; }
    public string? ContactNumber { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public List<Branch> Branches { get; set; } = new();
}

public class Branch : BaseEntity
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Site> Sites { get; set; } = new();
}

public class Site : BaseEntity
{
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>API key used by the site gateway service to authenticate sync calls.</summary>
    public string GatewayApiKey { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? LastHeartbeatAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public int PendingSyncCount { get; set; }
    public List<BiometricDevice> Devices { get; set; } = new();
}

public class Department : BaseEntity
{
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? HeadEmployeeId { get; set; }
    public Employee? HeadEmployee { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Position : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Holiday : BaseEntity
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public HolidayType Type { get; set; }
    public bool IsRecurringYearly { get; set; }
}
