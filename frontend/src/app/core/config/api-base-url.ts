import { InjectionToken } from '@angular/core';
import { environment } from '../../../environments/environment';

/** Root URL of the SmartLedger API. A token rather than a direct import, so tests can point it elsewhere. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});
