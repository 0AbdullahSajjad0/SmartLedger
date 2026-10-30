import { HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Injectable, effect, inject } from '@angular/core';
import { of, tap } from 'rxjs';
import { AuthStore } from '../auth/auth-store';

/** About the receipts someone flips between; previews are up to 3072 px, so this stays at a few megabytes. */
const MAX_ENTRIES = 12;

/** A receipt's preview never changes once the server has made it, so a copy can be reused as it is. */
const PREVIEW_URL = /\/api\/receipts\/[^/]+\/preview$/;

/**
 * Keeps the last few receipt previews in memory, so going back to a receipt shows its picture at once instead of
 * downloading it again. Memory only, on purpose: receipts are private, and a long-lived browser cache would leave
 * them on the disk of a shared computer. Emptied on sign-out; gone when the tab closes.
 */
@Injectable({ providedIn: 'root' })
export class ReceiptImageCache {
  /** Insertion order is use order: the first entry is the least recently used. */
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
      // Used again: move it to the most recent end.
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

/** Answers repeat requests for a receipt preview from {@link ReceiptImageCache}; everything else passes through. */
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
