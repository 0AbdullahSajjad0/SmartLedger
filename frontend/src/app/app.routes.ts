import { Routes } from '@angular/router';
import { signedInGuard, signedOutGuard } from './core/auth/auth.guards';

// Every page is lazy-loaded: the browser downloads a screen's code only when it's first visited.
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
    ],
  },
  { path: '**', redirectTo: '' },
];
