import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { AuthResponse, RegisterRequest, SignInRequest, User } from './auth.models';

/** Marks requests the auth interceptor must leave alone (they manage tokens themselves). */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

const skipAuth = () => ({ context: new HttpContext().set(SKIP_AUTH, true) });

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${inject(API_BASE_URL)}/api/auth`;

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/register`, request, skipAuth());
  }

  signIn(request: SignInRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/login`, request, skipAuth());
  }

  refresh(refreshToken: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/refresh`, { refreshToken }, skipAuth());
  }

  signOut(refreshToken: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/logout`, { refreshToken }, skipAuth());
  }

  /** Always succeeds, whether or not an account uses the address. */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/forgot-password`, { email }, skipAuth());
  }

  resetPassword(userId: string, token: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reset-password`, { userId, token, newPassword }, skipAuth());
  }

  /** Works signed in or not: the token in the link says whose address it is. */
  confirmEmail(userId: string, token: string): Observable<User> {
    return this.http.post<User>(`${this.baseUrl}/confirm-email`, { userId, token }, skipAuth());
  }

  me(): Observable<User> {
    return this.http.get<User>(`${this.baseUrl}/me`);
  }
}
