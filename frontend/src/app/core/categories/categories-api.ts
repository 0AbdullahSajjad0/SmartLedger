import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { Category } from '../receipts/receipt.models';
import { CategoryRequest, UpdateCategoryRequest } from './category.models';

@Injectable({ providedIn: 'root' })
export class CategoriesApi {
  private readonly http = inject(HttpClient);

  /** Also used by pages that load data through `httpResource`. */
  readonly baseUrl = `${inject(API_BASE_URL)}/api/categories`;

  /** Every category with its spending in a month ("yyyy-MM"). */
  readonly spendingUrl = `${this.baseUrl}/spending`;

  create(request: CategoryRequest): Observable<Category> {
    return this.http.post<Category>(this.baseUrl, request);
  }

  /** Renames, recolours, sets the budget, or archives and unarchives. */
  update(id: string, request: UpdateCategoryRequest): Observable<Category> {
    return this.http.put<Category>(`${this.baseUrl}/${id}`, request);
  }
}
