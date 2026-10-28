import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, numberAttribute } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PagedResult, ReceiptStatus, ReceiptSummary } from '../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../core/receipts/receipts-api';
import { describeHttpError } from '../../core/http/describe-http-error';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';

const PAGE_SIZE = 25;
const REFRESH_WHILE_READING_MS = 4_000;

interface StatusTab {
  label: string;
  status: ReceiptStatus | null;
}

const TABS: readonly StatusTab[] = [
  { label: 'All', status: null },
  { label: 'Reading', status: 'Processing' },
  { label: 'Needs review', status: 'NeedsReview' },
  { label: 'Approved', status: 'Approved' },
  { label: 'Rejected', status: 'Rejected' },
  { label: 'Unreadable', status: 'Failed' },
];

const KNOWN_STATUSES = new Set(TABS.map((tab) => tab.status).filter((status) => status !== null));

/**
 * The ledger of the user's receipts. Filter and page live in the URL (?status=NeedsReview&page=2), so a view
 * can be bookmarked or shared, and the back button works. They arrive as inputs through router binding.
 */
@Component({
  selector: 'app-receipts',
  imports: [RouterLink, DatePipe, DecimalPipe, EntryNumberPipe, StatusStamp],
  templateUrl: './receipts.html',
  styleUrl: './receipts.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Receipts {
  private readonly api = inject(ReceiptsApi);

  /** Query parameter; anything that isn't a known status means "all". */
  readonly status = input<ReceiptStatus | null, string | undefined>(null, {
    transform: (value: string | undefined): ReceiptStatus | null =>
      KNOWN_STATUSES.has(value as ReceiptStatus) ? (value as ReceiptStatus) : null,
  });

  /** Query parameter; starts at 1. */
  readonly page = input(1, { transform: (value: unknown) => Math.max(1, numberAttribute(value, 1)) });

  protected readonly tabs = TABS;

  /** Re-fetches by itself whenever the status or page changes. */
  protected readonly ledger = httpResource<PagedResult<ReceiptSummary>>(() => ({
    url: this.api.baseUrl,
    params: {
      page: this.page(),
      pageSize: PAGE_SIZE,
      ...(this.status() ? { status: this.status()! } : {}),
    },
  }));

  protected readonly totalPages = computed(() =>
    this.ledger.hasValue() ? Math.max(1, Math.ceil(this.ledger.value().totalCount / PAGE_SIZE)) : 1,
  );

  protected readonly range = computed(() => {
    if (!this.ledger.hasValue() || this.ledger.value().totalCount === 0) {
      return null;
    }
    const { page, totalCount, items } = this.ledger.value();
    const first = (page - 1) * PAGE_SIZE + 1;
    return { first, last: first + items.length - 1, total: totalCount };
  });

  protected readonly errorMessage = computed(() => (this.ledger.error() ? describeHttpError(this.ledger.error()) : null));

  /** True while AI is still reading something on this page. */
  private readonly anyBeingRead = computed(
    () => this.ledger.hasValue() && this.ledger.value().items.some((receipt) => receipt.status === 'Processing'),
  );

  constructor() {
    // Refresh quietly every few seconds while a receipt is being read; stop as soon as none is.
    effect((onCleanup) => {
      if (!this.anyBeingRead()) {
        return;
      }
      const timer = setInterval(() => this.ledger.reload(), REFRESH_WHILE_READING_MS);
      onCleanup(() => clearInterval(timer));
    });
  }
}
