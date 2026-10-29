import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from '../config/api-base-url';

@Injectable({ providedIn: 'root' })
export class OverviewApi {
  /** `?month=yyyy-MM`, loaded through `httpResource`. */
  readonly url = `${inject(API_BASE_URL)}/api/overview`;
}
