import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { Category } from '../receipts/receipt.models';
import { CategoryRequest, UpdateCategoryRequest } from './category.models';

@Injectable({ providedIn: 'root' })
export class CategoriesApi {
  private readonly http = inject(HttpClient);

  readonly baseUrl = `${inject(API_BASE_URL)}/api/categories`;

  readonly spendingUrl = `${this.baseUrl}/spending`;

  create(request: CategoryRequest): Observable<Category> {
    return this.http.post<Category>(this.baseUrl, request);
  }

  update(id: string, request: UpdateCategoryRequest): Observable<Category> {
    return this.http.put<Category>(`${this.baseUrl}/${id}`, request);
  }
}
