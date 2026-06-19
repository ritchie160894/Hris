import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isLoggedIn()) return true;
  router.navigate(['/login']);
  return false;
};

export const EXEC_ROLES = ['VicePresidentHrHead', 'PresidentCeo'];

/** Executives have an approval-only portal: redirect them to /approvals. */
export const nonExecGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isLoggedIn()) { router.navigate(['/login']); return false; }
  if (!auth.hasRole(...EXEC_ROLES)) return true;
  router.navigate(['/approvals']);
  return false;
};

/** Restrict a route to specific roles, e.g. roleGuard('SuperAdministrator', 'HrAdministrator'). */
export function roleGuard(...roles: string[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.isLoggedIn()) { router.navigate(['/login']); return false; }
    if (auth.hasRole(...roles)) return true;
    router.navigate(['/dashboard']);
    return false;
  };
}
