import { Routes } from '@angular/router';
import { signedInGuard, signedOutGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'sign-in',
    canMatch: [signedOutGuard],
    loadComponent: () => import('./layout/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        title: 'Sign in · SmartLedger',
        loadComponent: () => import('./features/auth/sign-in/sign-in').then((m) => m.SignIn),
      },
    ],
  },
  {
    path: 'register',
    canMatch: [signedOutGuard],
    loadComponent: () => import('./layout/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        title: 'Open an account · SmartLedger',
        loadComponent: () => import('./features/auth/register/register').then((m) => m.Register),
      },
    ],
  },
  {
    path: 'forgot-password',
    canMatch: [signedOutGuard],
    loadComponent: () => import('./layout/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        title: 'Reset password · SmartLedger',
        loadComponent: () => import('./features/auth/forgot-password/forgot-password').then((m) => m.ForgotPassword),
      },
    ],
  },
  {
    path: 'reset-password',
    canMatch: [signedOutGuard],
    loadComponent: () => import('./layout/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        title: 'New password · SmartLedger',
        loadComponent: () => import('./features/auth/reset-password/reset-password').then((m) => m.ResetPassword),
      },
    ],
  },
  {
    path: 'confirm-email',
    loadComponent: () => import('./layout/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        title: 'Confirm email · SmartLedger',
        loadComponent: () => import('./features/auth/confirm-email/confirm-email').then((m) => m.ConfirmEmail),
      },
    ],
  },
  {
    path: '',
    canMatch: [signedInGuard],
    loadComponent: () => import('./layout/app-shell/app-shell').then((m) => m.AppShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        title: 'Overview · SmartLedger',
        loadComponent: () => import('./features/overview/overview').then((m) => m.Overview),
      },
      {
        path: 'receipts',
        title: 'Receipts · SmartLedger',
        loadComponent: () => import('./features/receipts/receipts').then((m) => m.Receipts),
      },
      {
        path: 'receipts/:id',
        title: 'Receipt · SmartLedger',
        loadComponent: () => import('./features/receipt/receipt-page').then((m) => m.ReceiptPage),
      },
      {
        path: 'scan',
        title: 'Scan receipts · SmartLedger',
        loadComponent: () => import('./features/scan/scan').then((m) => m.Scan),
      },
      {
        path: 'reports',
        title: 'Reports · SmartLedger',
        loadComponent: () => import('./features/reports/reports-page').then((m) => m.ReportsPage),
      },
      {
        path: 'categories',
        title: 'Categories & budgets · SmartLedger',
        loadComponent: () => import('./features/categories/categories-page').then((m) => m.CategoriesPage),
      },
      {
        path: 'account',
        title: 'Account · SmartLedger',
        loadComponent: () => import('./features/account/account-page').then((m) => m.AccountPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
