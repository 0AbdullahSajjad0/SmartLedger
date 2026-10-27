// Mirrors SmartLedger.Application.Auth contracts on the API.

export interface User {
  id: string;
  email: string;
  displayName: string;
  baseCurrency: string;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: User;
}

export interface SignInRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  baseCurrency: string;
}

/** `restoring` while a saved session is being checked on page load. */
export type SessionStatus = 'restoring' | 'signed-in' | 'signed-out';
