import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { User } from '../auth/auth.models';
import { TokenStorage } from '../auth/token-storage';
import { API_BASE_URL } from '../config/api-base-url';

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(TokenStorage);
  private readonly url = `${inject(API_BASE_URL)}/api/account`;

  updateProfile(displayName: string): Observable<User> {
    return this.http.put<User>(this.url, { displayName });
  }

  sendConfirmationEmail(): Observable<void> {
    return this.http.post<void>(`${this.url}/confirmation-email`, null);
  }

  updateNotifications(budgetAlerts: boolean): Observable<User> {
    return this.http.put<User>(`${this.url}/notifications`, { budgetAlerts });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.url}/password`, { currentPassword, newPassword, refreshToken: this.storage.get() });
  }

  signOutOtherDevices(): Observable<void> {
    return this.http.post<void>(`${this.url}/sign-out-others`, { refreshToken: this.storage.get() });
  }

  export(): Observable<Blob> {
    return this.http.get(`${this.url}/export`, { responseType: 'blob' });
  }

  delete(password: string): Observable<void> {
    return this.http.post<void>(`${this.url}/delete`, { password });
  }
}
