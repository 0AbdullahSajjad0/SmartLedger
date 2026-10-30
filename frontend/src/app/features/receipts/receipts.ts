import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal, numberAttribute } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Category, PagedResult, ReceiptStatus, ReceiptSummary } from '../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../core/receipts/receipts-api';
import { describeHttpError } from '../../core/http/describe-http-error';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';

const PAGE_SIZE = 25;
const REFRESH_WHILE_READING_MS = 4_000;
const SEARCH_DELAY_MS = 350;

function isDate(value: string | undefined): value is string {
  return !!value && /^\d{4}-\d{2}-\d{2}$/.test(value);
}

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

@Component({
  selector: 'app-receipts',
  imports: [RouterLink, DatePipe, DecimalPipe, EntryNumberPipe, StatusStamp],
  templateUrl: './receipts.html',
  styleUrl: './receipts.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Receipts {
  private readonly api = inject(ReceiptsApi);
  private readonly router = inject(Router);

  readonly status = input<ReceiptStatus | null, string | undefined>(null, {
    transform: (value: string | undefined): ReceiptStatus | null =>
      KNOWN_STATUSES.has(value as ReceiptStatus) ? (value as ReceiptStatus) : null,
  });

  readonly page = input(1, { transform: (value: unknown) => Math.max(1, numberAttribute(value, 1)) });

  readonly search = input<string, string | undefined>('', { transform: (value) => value?.trim() ?? '' });
  readonly category = input<string | null, string | undefined>(null, { transform: (value) => value || null });
  readonly from = input<string | null, string | undefined>(null, { transform: (value) => (isDate(value) ? value : null) });
  readonly to = input<string | null, string | undefined>(null, { transform: (value) => (isDate(value) ? value : null) });

  protected readonly tabs = TABS;

  protected readonly ledger = httpResource<PagedResult<ReceiptSummary>>(() => ({
    url: this.api.baseUrl,
    params: {
      page: this.page(),
      pageSize: PAGE_SIZE,
      ...(this.status() ? { status: this.status()! } : {}),
      ...(this.search() ? { search: this.search() } : {}),
      ...(this.category() ? { categoryId: this.category()! } : {}),
      ...(this.from() ? { from: this.from()! } : {}),
      ...(this.to() ? { to: this.to()! } : {}),
    },
  }));

  protected readonly categories = httpResource<Category[]>(() => this.api.categoriesUrl, { defaultValue: [] });

  protected readonly filtered = computed(() => !!(this.search() || this.category() || this.from() || this.to()));

  protected readonly searchText = linkedSignal(() => this.search());
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected categoryOf(id: string | null): Category | null {
    return id ? (this.categories.value().find((c) => c.id === id) ?? null) : null;
  }

  protected onSearch(text: string): void {
    this.searchText.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.filterBy({ search: text.trim() || null }, true), SEARCH_DELAY_MS);
  }

  protected filterBy(params: Record<string, string | null>, replaceUrl = false): void {
    void this.router.navigate([], { queryParams: { ...params, page: null }, queryParamsHandling: 'merge', replaceUrl });
  }

  protected clearFilters(): void {
    clearTimeout(this.searchTimer);
    this.filterBy({ search: null, category: null, from: null, to: null });
  }

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

  private readonly anyBeingRead = computed(
    () => this.ledger.hasValue() && this.ledger.value().items.some((receipt) => receipt.status === 'Processing'),
  );

  constructor() {
    effect((onCleanup) => {
      if (!this.anyBeingRead()) {
        return;
      }
      const timer = setInterval(() => this.ledger.reload(), REFRESH_WHILE_READING_MS);
      onCleanup(() => clearInterval(timer));
    });
  }
}
