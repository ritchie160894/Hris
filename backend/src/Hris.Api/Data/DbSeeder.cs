using Hris.Domain;
using Hris.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hris.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(HrisDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync()) return;

        // ---- Organization ----
        var company = new Company { Name = "Acme Holdings Inc.", LegalName = "Acme Holdings Incorporated", Tin = "000-000-000-000", Address = "Makati City, Philippines" };
        var mainBranch = new Branch { Company = company, Code = "HO", Name = "Head Office", Address = "Makati City" };
        var cebuBranch = new Branch { Company = company, Code = "CEB", Name = "Cebu Branch", Address = "Cebu City" };
        var mainSite = new Site { Branch = mainBranch, Code = "HO-S1", Name = "Head Office - Main Site", Address = "Makati City", GatewayApiKey = "b79469bc815040ea9d6fc21eb68c91f3" };
        var cebuSite = new Site { Branch = cebuBranch, Code = "CEB-S1", Name = "Cebu - Plant Site", Address = "Cebu City", GatewayApiKey = "ceb9site0gateway00000000000000001" };
        db.AddRange(company, mainBranch, cebuBranch, mainSite, cebuSite);

        var deptHr = new Department { Branch = mainBranch, Code = "HR", Name = "Human Resources" };
        var deptIt = new Department { Branch = mainBranch, Code = "IT", Name = "Information Technology" };
        var deptOps = new Department { Branch = cebuBranch, Code = "OPS", Name = "Operations" };
        var deptFin = new Department { Branch = mainBranch, Code = "FIN", Name = "Finance" };
        db.AddRange(deptHr, deptIt, deptOps, deptFin);

        var posCeo = new Position { Code = "CEO", Title = "President & CEO" };
        var posVp = new Position { Code = "VP-HR", Title = "Vice President & HR Head", Department = deptHr };
        var posHrOfficer = new Position { Code = "HR-OFF", Title = "HR Officer", Department = deptHr };
        var posDevLead = new Position { Code = "IT-LEAD", Title = "IT Department Head", Department = deptIt };
        var posDev = new Position { Code = "IT-DEV", Title = "Software Developer", Department = deptIt };
        var posOpsHead = new Position { Code = "OPS-HEAD", Title = "Operations Department Head", Department = deptOps };
        var posOpsStaff = new Position { Code = "OPS-STF", Title = "Operations Staff", Department = deptOps };
        var posPayroll = new Position { Code = "FIN-PAY", Title = "Payroll Officer", Department = deptFin };
        db.AddRange(posCeo, posVp, posHrOfficer, posDevLead, posDev, posOpsHead, posOpsStaff, posPayroll);

        // ---- Employees ----
        Employee MakeEmp(string code, string first, string last, Department d, Position p, Branch br, Site s, decimal salary, Employee? mgr = null) => new()
        {
            EmployeeCode = code, FirstName = first, LastName = last,
            HireDate = new DateOnly(2022, 1, 10), Status = EmploymentStatus.Regular,
            Department = d, Position = p, Branch = br, Site = s,
            // HR policy: Basic Salary / 24 days = daily rate
            MonthlySalary = salary, DailyRate = Math.Round(salary / 24m, 2),
            Manager = mgr, BiometricUserId = code,
            Email = $"{first.ToLower()}.{last.ToLower().Replace(" ", "")}@acme.ph",
            SssNumber = "34-" + Random.Shared.Next(1000000, 9999999) + "-5",
            PhilHealthNumber = "08-" + Random.Shared.Next(100000000, 999999999) + "-1",
            PagIbigNumber = "1210-" + Random.Shared.Next(1000, 9999) + "-" + Random.Shared.Next(1000, 9999),
            Tin = Random.Shared.Next(100, 999) + "-" + Random.Shared.Next(100, 999) + "-" + Random.Shared.Next(100, 999)
        };

        var ceo = MakeEmp("EMP-0001", "Ramon", "Villanueva", deptHr, posCeo, mainBranch, mainSite, 350000);
        var vp = MakeEmp("EMP-0002", "Carmela", "Reyes", deptHr, posVp, mainBranch, mainSite, 180000, ceo);
        var hrOfficer = MakeEmp("EMP-0003", "Liza", "Mendoza", deptHr, posHrOfficer, mainBranch, mainSite, 45000, vp);
        var itHead = MakeEmp("EMP-0004", "Marco", "Santos", deptIt, posDevLead, mainBranch, mainSite, 95000, vp);
        var dev1 = MakeEmp("EMP-0005", "Juan", "Dela Cruz", deptIt, posDev, mainBranch, mainSite, 55000, itHead);
        var dev2 = MakeEmp("EMP-0006", "Maria", "Santos", deptIt, posDev, mainBranch, mainSite, 52000, itHead);
        var opsHead = MakeEmp("EMP-0007", "Pedro", "Garcia", deptOps, posOpsHead, cebuBranch, cebuSite, 75000, vp);
        var ops1 = MakeEmp("EMP-0008", "Ana", "Lopez", deptOps, posOpsStaff, cebuBranch, cebuSite, 28000, opsHead);
        var ops2 = MakeEmp("EMP-0009", "Jose", "Ramos", deptOps, posOpsStaff, cebuBranch, cebuSite, 28000, opsHead);
        var payrollOfficer = MakeEmp("EMP-0010", "Grace", "Tan", deptFin, posPayroll, mainBranch, mainSite, 48000, vp);
        var employees = new[] { ceo, vp, hrOfficer, itHead, dev1, dev2, opsHead, ops1, ops2, payrollOfficer };
        db.AddRange(employees);

        foreach (var e in employees)
            db.Add(new EmployeeHistory { Employee = e, EventType = "Hired", Description = $"Hired as {e.Position?.Title}", EffectiveDate = e.HireDate, ChangedByUserName = "system" });

        // ---- Users ----
        string Hash(string pw) => BCrypt.Net.BCrypt.HashPassword(pw);
        var users = new List<User>
        {
            new() { Username = "admin", PasswordHash = Hash("Admin@123"), Role = UserRole.SuperAdministrator, DisplayName = "System Administrator", Email = "admin@acme.ph" },
            new() { Username = "hradmin", PasswordHash = Hash("Hr@12345"), Role = UserRole.HrAdministrator, DisplayName = "HR Administrator", Employee = hrOfficer, Email = "hr@acme.ph" },
            new() { Username = "hrofficer", PasswordHash = Hash("Hr@12345"), Role = UserRole.HrOfficer, DisplayName = "Liza Mendoza", Employee = hrOfficer, Email = hrOfficer.Email },
            new() { Username = "payroll", PasswordHash = Hash("Pay@12345"), Role = UserRole.PayrollOfficer, DisplayName = "Grace Tan", Employee = payrollOfficer, Email = payrollOfficer.Email },
            new() { Username = "ithead", PasswordHash = Hash("Head@12345"), Role = UserRole.DepartmentHead, DisplayName = "Marco Santos", Employee = itHead, Email = itHead.Email },
            new() { Username = "opshead", PasswordHash = Hash("Head@12345"), Role = UserRole.DepartmentHead, DisplayName = "Pedro Garcia", Employee = opsHead, Email = opsHead.Email },
            new() { Username = "vp", PasswordHash = Hash("Vp@12345"), Role = UserRole.VicePresidentHrHead, DisplayName = "Carmela Reyes", Employee = vp, Email = vp.Email },
            new() { Username = "ceo", PasswordHash = Hash("Ceo@12345"), Role = UserRole.PresidentCeo, DisplayName = "Ramon Villanueva", Employee = ceo, Email = ceo.Email },
            new() { Username = "juan", PasswordHash = Hash("Emp@12345"), Role = UserRole.Employee, DisplayName = "Juan Dela Cruz", Employee = dev1, Email = dev1.Email },
            new() { Username = "maria", PasswordHash = Hash("Emp@12345"), Role = UserRole.Employee, DisplayName = "Maria Santos", Employee = dev2, Email = dev2.Email },
        };
        db.AddRange(users);

        // ---- Leave types and balances ----
        // Policy: Emergency Leave 10 days/year; SIL 5 days/year (Regular only, cash-convertible).
        var el = new LeaveType { Code = "EL", Name = "Emergency Leave", Category = LeaveCategory.Emergency, DefaultAnnualCredits = 10 };
        var sil = new LeaveType { Code = "SIL", Name = "Service Incentive Leave", Category = LeaveCategory.ServiceIncentive, DefaultAnnualCredits = 5, RequiresCeoApproval = true, IsConvertibleToCash = true, RegularEmployeesOnly = true };
        db.AddRange(el, sil);

        var year = DateTime.UtcNow.Year;
        foreach (var e in employees)
        {
            db.Add(new LeaveBalance { Employee = e, LeaveType = el, Year = year, Credits = 10 });
            if (e.Status == EmploymentStatus.Regular)
                db.Add(new LeaveBalance { Employee = e, LeaveType = sil, Year = year, Credits = sil.DefaultAnnualCredits });
        }

        // ---- Approval workflow templates (per Executive Approval Portal spec) ----
        var steps = new List<WorkflowTemplateStep>
        {
            // Leave: Dept Head -> HR Officer -> VP & HR Head -> President & CEO
            new() { RequestType = RequestType.Leave, Level = 1, ApproverRole = UserRole.DepartmentHead, StepName = "Department Head" },
            new() { RequestType = RequestType.Leave, Level = 2, ApproverRole = UserRole.HrOfficer, StepName = "HR Officer" },
            new() { RequestType = RequestType.Leave, Level = 3, ApproverRole = UserRole.VicePresidentHrHead, StepName = "Vice President & HR Head" },
            new() { RequestType = RequestType.Leave, Level = 4, ApproverRole = UserRole.PresidentCeo, StepName = "President & CEO" },
            // SIL: VP & HR Head -> President & CEO
            new() { RequestType = RequestType.ServiceIncentiveLeave, Level = 1, ApproverRole = UserRole.VicePresidentHrHead, StepName = "Vice President & HR Head" },
            new() { RequestType = RequestType.ServiceIncentiveLeave, Level = 2, ApproverRole = UserRole.PresidentCeo, StepName = "President & CEO" },
            // Overtime: Dept Head -> HR Officer -> VP & HR Head (CEO optional)
            new() { RequestType = RequestType.Overtime, Level = 1, ApproverRole = UserRole.DepartmentHead, StepName = "Department Head" },
            new() { RequestType = RequestType.Overtime, Level = 2, ApproverRole = UserRole.HrOfficer, StepName = "HR Officer" },
            new() { RequestType = RequestType.Overtime, Level = 3, ApproverRole = UserRole.VicePresidentHrHead, StepName = "Vice President & HR Head" },
            // Cash advance: Dept Head -> HR Officer -> VP & HR Head
            new() { RequestType = RequestType.CashAdvance, Level = 1, ApproverRole = UserRole.DepartmentHead, StepName = "Department Head" },
            new() { RequestType = RequestType.CashAdvance, Level = 2, ApproverRole = UserRole.HrOfficer, StepName = "HR Officer" },
            new() { RequestType = RequestType.CashAdvance, Level = 3, ApproverRole = UserRole.VicePresidentHrHead, StepName = "Vice President & HR Head" },
            // Loan: Dept Head -> HR Officer -> VP & HR Head
            new() { RequestType = RequestType.Loan, Level = 1, ApproverRole = UserRole.DepartmentHead, StepName = "Department Head" },
            new() { RequestType = RequestType.Loan, Level = 2, ApproverRole = UserRole.HrOfficer, StepName = "HR Officer" },
            new() { RequestType = RequestType.Loan, Level = 3, ApproverRole = UserRole.VicePresidentHrHead, StepName = "Vice President & HR Head" },
            // Attendance correction: Dept Head -> HR Officer -> Payroll Officer (apply)
            new() { RequestType = RequestType.AttendanceCorrection, Level = 1, ApproverRole = UserRole.DepartmentHead, StepName = "Department Head / Supervisor" },
            new() { RequestType = RequestType.AttendanceCorrection, Level = 2, ApproverRole = UserRole.HrOfficer, StepName = "HR Officer" },
            new() { RequestType = RequestType.AttendanceCorrection, Level = 3, ApproverRole = UserRole.PayrollOfficer, StepName = "Payroll Officer", IsApplyOnlyStep = true },
            // OT correction: Dept Head -> HR Officer -> Payroll Officer (apply)
            new() { RequestType = RequestType.OvertimeCorrection, Level = 1, ApproverRole = UserRole.DepartmentHead, StepName = "Department Head" },
            new() { RequestType = RequestType.OvertimeCorrection, Level = 2, ApproverRole = UserRole.HrOfficer, StepName = "HR Officer" },
            new() { RequestType = RequestType.OvertimeCorrection, Level = 3, ApproverRole = UserRole.PayrollOfficer, StepName = "Payroll Officer", IsApplyOnlyStep = true },
            // Payroll: Payroll Officer -> VP & HR Head
            new() { RequestType = RequestType.Payroll, Level = 1, ApproverRole = UserRole.VicePresidentHrHead, StepName = "Vice President & HR Head" },
            new() { RequestType = RequestType.Payroll, Level = 2, ApproverRole = UserRole.PresidentCeo, StepName = "President & CEO" },
        };
        db.AddRange(steps);

        // ---- Government tables (Philippines — maintainable from Government module UI) ----
        // SSS 2025 schedule: 15% of MSC (5% EE / 10% ER), MSC ₱5,000–₱35,000.
        for (decimal msc = 5000; msc <= 35000; msc += 500)
        {
            var from = msc - 250;
            var to = msc == 35000 ? 9999999 : msc + 249.99m;
            db.Add(new SssBracket
            {
                RangeFrom = from < 0 ? 0 : from, RangeTo = to, MonthlySalaryCredit = msc,
                EmployeeShare = Math.Round(msc * 0.05m, 2), EmployerShare = Math.Round(msc * 0.10m, 2),
                EffectiveYear = year
            });
        }
        // PhilHealth: 5% premium on monthly basic, 50/50 split, ₱10k–₱100k basis.
        db.Add(new PhilHealthConfig { EffectiveYear = year, RatePercent = 5.0m, MinSalary = 10000, MaxSalary = 100000, EmployeeSharePercent = 50 });
        // Pag-IBIG: EE 1% (≤₱1,500) or 2% (>₱1,500), ER 2%, basis capped at ₱10,000.
        db.Add(new PagIbigConfig { EffectiveYear = year, EmployeeRatePercent = 2, EmployerRatePercent = 2, MaxCompensation = 10000, EmployeeLowRatePercent = 1, EmployeeLowThreshold = 1500 });

        // BIR TRAIN Law monthly withholding tax table (2023 onwards).
        db.AddRange(
            new TaxBracket { EffectiveYear = year, RangeFrom = 0, RangeTo = 20833, BaseTax = 0, RatePercentOverExcess = 0 },
            new TaxBracket { EffectiveYear = year, RangeFrom = 20833.01m, RangeTo = 33332, BaseTax = 0, RatePercentOverExcess = 15 },
            new TaxBracket { EffectiveYear = year, RangeFrom = 33332.01m, RangeTo = 66666, BaseTax = 1875, RatePercentOverExcess = 20 },
            new TaxBracket { EffectiveYear = year, RangeFrom = 66666.01m, RangeTo = 166666, BaseTax = 8541.80m, RatePercentOverExcess = 25 },
            new TaxBracket { EffectiveYear = year, RangeFrom = 166666.01m, RangeTo = 666666, BaseTax = 33541.80m, RatePercentOverExcess = 30 },
            new TaxBracket { EffectiveYear = year, RangeFrom = 666666.01m, RangeTo = 999999999, BaseTax = 183541.80m, RatePercentOverExcess = 35 });

        // ---- Holidays (PH sample) ----
        db.AddRange(
            new Holiday { Date = new DateOnly(year, 1, 1), Name = "New Year's Day", Type = HolidayType.Regular, IsRecurringYearly = true },
            new Holiday { Date = new DateOnly(year, 4, 9), Name = "Araw ng Kagitingan", Type = HolidayType.Regular, IsRecurringYearly = true },
            new Holiday { Date = new DateOnly(year, 5, 1), Name = "Labor Day", Type = HolidayType.Regular, IsRecurringYearly = true },
            new Holiday { Date = new DateOnly(year, 6, 12), Name = "Independence Day", Type = HolidayType.Regular, IsRecurringYearly = true },
            new Holiday { Date = new DateOnly(year, 8, 21), Name = "Ninoy Aquino Day", Type = HolidayType.SpecialNonWorking, IsRecurringYearly = true },
            new Holiday { Date = new DateOnly(year, 11, 30), Name = "Bonifacio Day", Type = HolidayType.Regular, IsRecurringYearly = true },
            new Holiday { Date = new DateOnly(year, 12, 25), Name = "Christmas Day", Type = HolidayType.Regular, IsRecurringYearly = true },
            new Holiday { Date = new DateOnly(year, 12, 30), Name = "Rizal Day", Type = HolidayType.Regular, IsRecurringYearly = true });

        // ---- Devices ----
        db.AddRange(
            new BiometricDevice { SerialNumber = "SF2A-HO-0001", Name = "Head Office Lobby", Site = mainSite, IpAddress = "192.168.1.201", Status = DeviceStatus.Offline },
            new BiometricDevice { SerialNumber = "SF2A-CEB-0001", Name = "Cebu Plant Gate", Site = cebuSite, IpAddress = "192.168.10.201", Status = DeviceStatus.Offline });

        // ---- Benefits ----
        db.AddRange(
            new Benefit { Name = "HMO - Maxicare Gold", Type = BenefitType.Hmo, Provider = "Maxicare", MonthlyCost = 2500 },
            new Benefit { Name = "Rice Allowance", Type = BenefitType.Allowance, MonthlyCost = 2000 },
            new Benefit { Name = "13th Month Pay", Type = BenefitType.Bonus, IsThirteenthMonth = true, Description = "Mandatory benefit (PD 851): total basic salary earned in the year ÷ 12, accrued each payroll." });

        // ---- Sample attendance for the past 5 working days ----
        var rnd = new Random(42);
        var today = DateTime.Today;
        for (var d = 7; d >= 1; d--)
        {
            var date = today.AddDays(-d);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            // VP & CEO are company owners — exempt from biometric timekeeping.
            foreach (var e in employees.Where(e => e != ceo && e != vp))
            {
                var timeIn = date.AddHours(7).AddMinutes(40 + rnd.Next(0, 45));
                var timeOut = date.AddHours(17).AddMinutes(rnd.Next(0, 60));
                db.Add(new AttendanceLog { Employee = e, Site = e.Site ?? mainSite, PunchTime = timeIn, PunchType = PunchType.TimeIn, Source = AttendanceSource.Biometric, VerifyMode = "face" });
                db.Add(new AttendanceLog { Employee = e, Site = e.Site ?? mainSite, PunchTime = timeOut, PunchType = PunchType.TimeOut, Source = AttendanceSource.Biometric, VerifyMode = "face" });
            }
        }

        // ---- Announcements ----
        db.AddRange(
            new Announcement { Type = AnnouncementType.Announcement, Title = "Welcome to the new HRIS", Body = "Our new HR Information System is now live. Use your issued credentials to log in.", PublishDate = DateOnly.FromDateTime(today), IsPinned = true, PostedByName = "HR Department" },
            new Announcement { Type = AnnouncementType.HolidayNotice, Title = "Independence Day Holiday", Body = "June 12 is a regular holiday. No work, with pay as per labor law.", PublishDate = DateOnly.FromDateTime(today), PostedByName = "HR Department" });

        await db.SaveChangesAsync();

        // Department heads are assigned after the initial save to avoid a
        // circular dependency between Department.HeadEmployeeId and Employee.DepartmentId.
        deptHr.HeadEmployee = vp;
        deptIt.HeadEmployee = itHead;
        deptOps.HeadEmployee = opsHead;
        await db.SaveChangesAsync();
    }
}
