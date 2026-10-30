import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { describeHttpError } from '../../core/http/describe-http-error';
import { Category, ReceiptDetail } from '../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../core/receipts/receipts-api';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { FileSizePipe } from '../../shared/formatting/file-size.pipe';
import { saveFile } from '../../shared/files/save-file';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';
import { numberFootnotes } from './footnotes';
import { ReceiptReviewForm } from './review-form/review-form';

const PREVIEWABLE = new Set(['image/jpeg', 'image/png', 'image/webp']);

const REFRESH_WHILE_READING_MS = 3_000;

@Component({
  selector: 'app-receipt-page',
  imports: [RouterLink, DatePipe, DecimalPipe, NgTemplateOutlet, EntryNumberPipe, FileSizePipe, StatusStamp, ReceiptReviewForm],
  templateUrl: './receipt-page.html',
  styleUrl: './receipt-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptPage {
  private readonly api = inject(ReceiptsApi);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly receipt = httpResource<ReceiptDetail>(() => `${this.api.baseUrl}/${this.id()}`);

  protected readonly categories = httpResource<Category[]>(() => this.api.categoriesUrl, { defaultValue: [] });

  protected readonly categoryName = computed(() => {
    const categoryId = this.receipt.hasValue() ? this.receipt.value().categoryId : null;
    return categoryId ? (this.categories.value().find((c) => c.id === categoryId)?.name ?? null) : null;
  });

  protected readonly footnotes = computed(() =>
    this.receipt.hasValue() ? numberFootnotes(this.receipt.value().extractionFlags) : [],
  );

  protected readonly baseCurrency = computed(() => this.auth.user()?.baseCurrency ?? 'USD');

  protected readonly editable = computed(() => {
    const status = this.receipt.hasValue() ? this.receipt.value().status : null;
    return status === 'NeedsReview' || status === 'Failed';
  });

  protected readonly reopening = signal(false);
  protected readonly reopenError = signal<string | null>(null);

  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  protected deleteReceipt(): void {
    if (!this.receipt.hasValue() || this.deleting()) {
      return;
    }
    this.deleting.set(true);
    this.deleteError.set(null);
    this.api
      .delete(this.receipt.value().id)
      .pipe(
        finalize(() => this.deleting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => void this.router.navigateByUrl('/receipts'),
        error: (error: unknown) => this.deleteError.set(describeHttpError(error)),
      });
  }

  protected footnoteFor(field: string): number | null {
    return this.editable() ? (this.footnotes().find((note) => note.field === field)?.number ?? null) : null;
  }

  protected readonly image = httpResource.blob(() => this.imageSource());

  private readonly imageSource = computed(() => {
    if (!this.receipt.hasValue()) {
      return undefined;
    }
    const { id, hasPreview, status, contentType } = this.receipt.value();
    if (hasPreview) {
      return this.api.previewUrl(id);
    }
    return status !== 'Processing' && PREVIEWABLE.has(contentType) ? this.api.fileUrl(id) : undefined;
  });

  protected readonly imageUrl = signal<string | null>(null);

  protected readonly originalIsPhoto = computed(() =>
    this.receipt.hasValue() ? this.receipt.value().contentType.startsWith('image/') : false,
  );

  protected readonly downloading = signal(false);
  protected readonly downloadError = signal<string | null>(null);

  protected readonly errorMessage = computed(() =>
    this.receipt.error() ? describeHttpError(this.receipt.error()) : null,
  );

  protected onChanged(receipt: ReceiptDetail): void {
    this.receipt.set(receipt);
  }

  protected reopen(): void {
    if (!this.receipt.hasValue() || this.reopening()) {
      return;
    }
    this.reopening.set(true);
    this.reopenError.set(null);
    this.api
      .reopen(this.receipt.value().id)
      .pipe(
        finalize(() => this.reopening.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (receipt) => this.receipt.set(receipt),
        error: (error: unknown) => this.reopenError.set(describeHttpError(error)),
      });
  }

  protected downloadOriginal(): void {
    if (!this.receipt.hasValue() || this.downloading()) {
      return;
    }
    const { id, number, fileName } = this.receipt.value();

    this.downloading.set(true);
    this.downloadError.set(null);
    this.api
      .downloadFile(id)
      .pipe(
        finalize(() => this.downloading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) => saveFile(blob, downloadName(number, fileName)),
        error: (error: unknown) => this.downloadError.set(describeHttpError(error)),
      });
  }

  constructor() {
    effect((onCleanup) => {
      const reading = this.receipt.hasValue() && this.receipt.value().status === 'Processing';
      if (!reading) {
        return;
      }
      const timer = setInterval(() => this.receipt.reload(), REFRESH_WHILE_READING_MS);
      onCleanup(() => clearInterval(timer));
    });

    effect((onCleanup) => {
      const blob = this.image.hasValue() ? this.image.value() : undefined;
      if (!blob) {
        this.imageUrl.set(null);
        return;
      }

      const url = URL.createObjectURL(blob);
      this.imageUrl.set(url);
      onCleanup(() => URL.revokeObjectURL(url));
    });
  }
}

function downloadName(number: number, fileName: string): string {
  const extension = fileName.includes('.') ? fileName.slice(fileName.lastIndexOf('.')).toLowerCase() : '';
  return `receipt-${number.toString().padStart(4, '0')}${extension}`;
}
