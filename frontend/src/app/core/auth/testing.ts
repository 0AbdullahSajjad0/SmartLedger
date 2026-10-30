import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider } from '@angular/core';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../config/api-base-url';
import { authInterceptor } from './auth.interceptor';
import { AuthResponse } from './auth.models';

export const TEST_API = 'http://api.test';

/** HTTP (with the auth interceptor and a fake backend), an empty router, and a fixed API address. */
export function provideAuthTesting(): (Provider | EnvironmentProviders)[] {
  return [
    provideHttpClient(withInterceptors([authInterceptor])),
    provideHttpClientTesting(),
    provideRouter([]),
    { provide: API_BASE_URL, useValue: TEST_API },
  ];
}

export function authResponse(accessToken: string, refreshToken: string): AuthResponse {
  const inMinutes = (minutes: number) => new Date(Date.now() + minutes * 60_000).toISOString();
  return {
    accessToken,
    accessTokenExpiresAt: inMinutes(15),
    refreshToken,
    refreshTokenExpiresAt: inMinutes(60 * 24 * 7),
    user: { id: 'user-1', email: 'ada@example.com', displayName: 'Ada Lovelace', baseCurrency: 'GBP', budgetAlerts: true },
  };
}
