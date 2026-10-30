export interface User {
  id: string;
  email: string;
  displayName: string;
  baseCurrency: string;
  budgetAlerts: boolean;
  emailConfirmed: boolean;
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

export type SessionStatus = 'restoring' | 'signed-in' | 'signed-out';
