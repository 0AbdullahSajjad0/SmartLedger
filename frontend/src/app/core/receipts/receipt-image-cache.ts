import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, effect, inject } from '@angular/core';
import { of, tap } from 'rxjs';
import { AuthStore } from '../auth/auth-store';

const MAX_ENTRIES = 12;

const PREVIEW_URL = /\/api\/receipts\/[^/]+\/preview$/;

@Injectable({ providedIn: 'root' })
export class ReceiptImageCache {
  private readonly entries = new Map<string, HttpResponse<Blob>>();

  constructor() {
    const auth = inject(AuthStore);
    effect(() => {
      if (auth.status() === 'signed-out') {
        this.clear();
      }
    });
  }

  get size(): number {
    return this.entries.size;
  }

  get(url: string): HttpResponse<Blob> | undefined {
    const hit = this.entries.get(url);
    if (hit) {
      this.entries.delete(url);
      this.entries.set(url, hit);
    }
    return hit;
  }

  set(url: string, response: HttpResponse<Blob>): void {
    this.entries.delete(url);
    this.entries.set(url, response);
    while (this.entries.size > MAX_ENTRIES) {
      this.entries.delete(this.entries.keys().next().value!);
    }
  }

  clear(): void {
    this.entries.clear();
  }
}

export const receiptImageCacheInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.method !== 'GET' || !PREVIEW_URL.test(request.url)) {
    return next(request);
  }

  const cache = inject(ReceiptImageCache);
  const hit = cache.get(request.url);
  if (hit) {
    return of(hit.clone());
  }

  return next(request).pipe(
    tap((event) => {
      if (event instanceof HttpResponse && event.ok && event.body instanceof Blob) {
        cache.set(request.url, event as HttpResponse<Blob>);
      }
    }),
  );
};
