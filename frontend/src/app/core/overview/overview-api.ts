import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from '../config/api-base-url';

@Injectable({ providedIn: 'root' })
export class OverviewApi {
  readonly url = `${inject(API_BASE_URL)}/api/overview`;
}
