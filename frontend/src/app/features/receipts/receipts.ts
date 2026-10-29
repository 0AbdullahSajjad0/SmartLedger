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

/**
 * The ledger of the user's receipts. Status, search, category, dates and page live in the URL
 * (?status=NeedsReview&search=chai&page=2), so a view can be bookmarked or shared, and the back button works. They
 * arrive as inputs through router binding.
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
  private readonly router = inject(Router);

  /** Query parameter; anything that isn't a known status means "all". */
  readonly status = input<ReceiptStatus | null, string | undefined>(null, {
    transform: (value: string | undefined): ReceiptStatus | null =>
      KNOWN_STATUSES.has(value as ReceiptStatus) ? (value as ReceiptStatus) : null,
  });

  /** Query parameter; starts at 1. */
  readonly page = input(1, { transform: (value: unknown) => Math.max(1, numberAttribute(value, 1)) });

  /** Query parameter: part of a vendor's name. */
  readonly search = input<string, string | undefined>('', { transform: (value) => value?.trim() ?? '' });
  /** Query parameter: a category id. */
  readonly category = input<string | null, string | undefined>(null, { transform: (value) => value || null });
  /** Query parameters: receipt dates, "yyyy-MM-dd". */
  readonly from = input<string | null, string | undefined>(null, { transform: (value) => (isDate(value) ? value : null) });
  readonly to = input<string | null, string | undefined>(null, { transform: (value) => (isDate(value) ? value : null) });

  protected readonly tabs = TABS;

  /** Re-fetches by itself whenever the status, page or a filter changes. */
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

  /** For the category column and the filter. */
  protected readonly categories = httpResource<Category[]>(() => this.api.categoriesUrl, { defaultValue: [] });

  protected readonly filtered = computed(() => !!(this.search() || this.category() || this.from() || this.to()));

  /** What's in the search box, ahead of the URL while typing. */
  protected readonly searchText = linkedSignal(() => this.search());
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected categoryOf(id: string | null): Category | null {
    return id ? (this.categories.value().find((c) => c.id === id) ?? null) : null;
  }

  /** Searches once typing pauses, rather than on every keystroke; replaces the history entry so Back still works. */
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
