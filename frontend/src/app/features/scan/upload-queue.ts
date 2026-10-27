import { HttpEventType } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { describeHttpError } from '../../core/http/describe-http-error';
import { RECEIPT_UPLOAD_LIMITS, ReceiptSummary } from '../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../core/receipts/receipts-api';

export type UploadState = 'queued' | 'uploading' | 'done' | 'failed';

export interface UploadItem {
  readonly key: number;
  readonly file: File;
  readonly state: UploadState;
  /** 0–1 while uploading. */
  readonly progress: number;
  readonly error?: string;
  /** False when the file itself is unacceptable (wrong type, too large): trying again can't help. */
  readonly retryable: boolean;
  readonly receipt?: ReceiptSummary;
}

/**
 * Uploads files one at a time, so each gets its own progress bar and its own error, and one bad file
 * never sinks the others. Provided by the Scan page, so the queue lives as long as the page does.
 */
@Injectable()
export class UploadQueue {
  private readonly api = inject(ReceiptsApi);
  private readonly destroyRef = inject(DestroyRef);

  private readonly _items = signal<readonly UploadItem[]>([]);
  private nextKey = 1;
  private running = false;

  /** Newest first. */
  readonly items = this._items.asReadonly();
  readonly busy = computed(() => this._items().some((item) => item.state === 'queued' || item.state === 'uploading'));

  add(files: readonly File[]): void {
    const accepted = files.slice(0, RECEIPT_UPLOAD_LIMITS.maxFilesPerUpload);
    const added = accepted.map((file): UploadItem => {
      const problem = checkFile(file);
      return {
        key: this.nextKey++,
        file,
        state: problem ? 'failed' : 'queued',
        progress: 0,
        error: problem ?? undefined,
        retryable: !problem,
      };
    });

    this._items.update((items) => [...added.reverse(), ...items]);
    void this.run();
  }

  retry(key: number): void {
    this.patch(key, { state: 'queued', progress: 0, error: undefined });
    void this.run();
  }

  dismiss(key: number): void {
    this._items.update((items) => items.filter((item) => item.key !== key));
  }

  private async run(): Promise<void> {
    if (this.running) {
      return;
    }
    this.running = true;

    try {
      // Oldest queued file first.
      let next: UploadItem | undefined;
      while ((next = [...this._items()].reverse().find((item) => item.state === 'queued'))) {
        await this.upload(next);
      }
    } finally {
      this.running = false;
    }
  }

  private upload(item: UploadItem): Promise<void> {
    this.patch(item.key, { state: 'uploading', progress: 0 });

    return new Promise((resolve) => {
      this.api
        .upload([item.file])
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (event) => {
            if (event.type === HttpEventType.UploadProgress && event.total) {
              this.patch(item.key, { progress: event.loaded / event.total });
            } else if (event.type === HttpEventType.Response) {
              this.patch(item.key, { state: 'done', progress: 1, receipt: event.body?.[0] });
            }
          },
          error: (error: unknown) => {
            // A server refusal may be temporary (rate limit, network, cold start), so offer a retry.
            this.patch(item.key, { state: 'failed', error: describeHttpError(error), retryable: true });
            resolve();
          },
          complete: () => resolve(),
        });
    });
  }

  private patch(key: number, changes: Partial<UploadItem>): void {
    this._items.update((items) => items.map((item) => (item.key === key ? { ...item, ...changes } : item)));
  }
}

/** The same checks the API makes, so obvious problems show immediately without a round trip. */
function checkFile(file: File): string | null {
  const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();

  if (!(RECEIPT_UPLOAD_LIMITS.extensions as readonly string[]).includes(extension)) {
    return 'Not a receipt image or PDF. Use JPG, PNG, WebP, HEIC or PDF.';
  }
  if (file.size === 0) {
    return 'The file is empty.';
  }
  if (file.size > RECEIPT_UPLOAD_LIMITS.maxSizeBytes) {
    return 'Larger than 10 MB. Try a smaller photo or a single-page PDF.';
  }
  return null;
}
