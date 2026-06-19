import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  styles: [`
    :host { display: flex; min-height: 100vh; align-items: center; justify-content: center;
      background: linear-gradient(135deg, #101728 0%, #1d2c4f 60%, #2456e6 100%); padding: 16px; }
    .box { background: #fff; border-radius: 16px; padding: 36px 40px 40px; width: 100%; max-width: 400px; box-shadow: 0 24px 80px rgba(0,0,0,.35); }
    .sub { color: var(--text-soft); font-size: 13.5px; margin-bottom: 24px; }
    .btn { width: 100%; justify-content: center; padding: 11px; font-size: 14.5px; margin-top: 6px; }
    .hint { margin-top: 20px; font-size: 12px; color: var(--text-faint); text-align: center; }
  `],
  template: `
    <div class="box">
      <div class="login-brand">
        <img src="logo-mark.svg" alt="Qcon" class="brand-mark" />
        <h1>Qcon HriSystem</h1>
        <div class="sub">Sign in to your account</div>
      </div>
      @if (error()) { <div class="alert error">{{ error() }}</div> }
      <form (ngSubmit)="submit()">
        <label class="field">
          <span class="lbl">Username</span>
          <input class="ctl" name="username" [(ngModel)]="username" required autocomplete="username" />
        </label>
        <label class="field">
          <span class="lbl">Password</span>
          <input class="ctl" type="password" name="password" [(ngModel)]="password" required autocomplete="current-password" />
        </label>
        <button class="btn" type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign In' }}</button>
      </form>
      <div class="hint">Multi-site attendance · Payroll · Executive approvals</div>
    </div>
  `
})
export class LoginComponent {
  username = '';
  password = '';
  busy = signal(false);
  error = signal('');

  constructor(private auth: AuthService, private router: Router) {}

  submit(): void {
    if (!this.username || !this.password) return;
    this.busy.set(true);
    this.error.set('');
    this.auth.login(this.username, this.password).subscribe({
      next: () => this.router.navigateByUrl(this.auth.homeRoute()),
      error: err => {
        this.busy.set(false);
        this.error.set(err?.error?.message ?? 'Login failed. Check the API is running.');
      }
    });
  }
}
