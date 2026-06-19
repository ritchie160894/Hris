import { Routes } from '@angular/router';
import { authGuard, nonExecGuard, roleGuard } from './core/guards';

const EXEC = ['VicePresidentHrHead', 'PresidentCeo'];
const ADMIN = ['SuperAdministrator', 'HrAdministrator'];
// Executives (VP & HR Head, President & CEO) have an approval-only portal and are
// intentionally excluded from these role lists.
const HR = ['SuperAdministrator', 'HrAdministrator', 'HrOfficer'];
const PAYROLL = ['SuperAdministrator', 'HrAdministrator', 'PayrollOfficer'];
const MANAGERS = ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'DepartmentHead', 'Supervisor', 'PayrollOfficer'];
const REPORTS = ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'PayrollOfficer', 'DepartmentHead'];

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/login.component').then(m => m.LoginComponent) },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', canActivate: [nonExecGuard], loadComponent: () => import('./features/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'approvals', loadComponent: () => import('./features/approvals.component').then(m => m.ApprovalsComponent) },
      { path: 'executive-payroll', canActivate: [roleGuard(...EXEC)], loadComponent: () => import('./features/executive-payroll.component').then(m => m.ExecutivePayrollComponent) },
      { path: 'employees', canActivate: [roleGuard(...MANAGERS)], loadComponent: () => import('./features/employees.component').then(m => m.EmployeesComponent) },
      { path: 'employees/:id', canActivate: [roleGuard(...MANAGERS)], loadComponent: () => import('./features/employee-detail.component').then(m => m.EmployeeDetailComponent) },
      { path: 'organization', canActivate: [roleGuard(...HR)], loadComponent: () => import('./features/organization.component').then(m => m.OrganizationComponent) },
      { path: 'attendance', canActivate: [nonExecGuard], loadComponent: () => import('./features/attendance.component').then(m => m.AttendanceComponent) },
      { path: 'leave', canActivate: [nonExecGuard], loadComponent: () => import('./features/leave.component').then(m => m.LeaveComponent) },
      { path: 'overtime', canActivate: [nonExecGuard], loadComponent: () => import('./features/overtime.component').then(m => m.OvertimeComponent) },
      { path: 'payroll', canActivate: [roleGuard(...PAYROLL)], loadComponent: () => import('./features/payroll.component').then(m => m.PayrollComponent) },
      { path: 'loans', canActivate: [nonExecGuard], loadComponent: () => import('./features/loans.component').then(m => m.LoansComponent) },
      { path: 'government', canActivate: [roleGuard(...PAYROLL)], loadComponent: () => import('./features/government.component').then(m => m.GovernmentComponent) },
      { path: 'benefits', canActivate: [nonExecGuard], loadComponent: () => import('./features/benefits.component').then(m => m.BenefitsComponent) },
      { path: 'recruitment', canActivate: [roleGuard(...HR)], loadComponent: () => import('./features/recruitment.component').then(m => m.RecruitmentComponent) },
      { path: 'performance', canActivate: [roleGuard(...MANAGERS)], loadComponent: () => import('./features/performance.component').then(m => m.PerformanceComponent) },
      { path: 'training', canActivate: [roleGuard(...HR)], loadComponent: () => import('./features/training.component').then(m => m.TrainingComponent) },
      { path: 'documents', canActivate: [nonExecGuard], loadComponent: () => import('./features/documents.component').then(m => m.DocumentsComponent) },
      { path: 'announcements', loadComponent: () => import('./features/announcements.component').then(m => m.AnnouncementsComponent) },
      { path: 'reports', canActivate: [roleGuard(...REPORTS)], loadComponent: () => import('./features/reports.component').then(m => m.ReportsComponent) },
      { path: 'devices', canActivate: [roleGuard(...ADMIN)], loadComponent: () => import('./features/devices.component').then(m => m.DevicesComponent) },
      { path: 'sync', canActivate: [roleGuard(...ADMIN)], loadComponent: () => import('./features/sync-monitor.component').then(m => m.SyncMonitorComponent) },
      { path: 'users', canActivate: [roleGuard(...ADMIN)], loadComponent: () => import('./features/users.component').then(m => m.UsersComponent) },
      { path: 'audit', canActivate: [roleGuard(...ADMIN)], loadComponent: () => import('./features/audit.component').then(m => m.AuditComponent) },
      { path: 'me', canActivate: [nonExecGuard], loadComponent: () => import('./features/ess.component').then(m => m.EssComponent) },
      { path: 'notifications', loadComponent: () => import('./features/notifications.component').then(m => m.NotificationsComponent) }
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
