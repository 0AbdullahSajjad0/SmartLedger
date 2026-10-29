import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CategoriesApi } from '../../core/categories/categories-api';
import { CategoryMonth, CategorySpending } from '../../core/categories/category.models';
import { describeHttpError } from '../../core/http/describe-http-error';
import { CategoryEditor } from './category-editor/category-editor';

/** "2026-10" for the month a date falls in. */
function monthOf(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

/** "2026-10" moved by whole months, e.g. -1 → "2026-09". */
function shiftMonth(month: string, by: number): string {
  const [year, monthNumber] = month.split('-').map(Number);
  return monthOf(new Date(year, monthNumber - 1 + by, 1));
}

const MONTH_NAME = new Intl.DateTimeFormat('en', { month: 'long', year: 'numeric' });
const SHORT_MONTH = new Intl.DateTimeFormat('en', { month: 'short' });

/** "2026-10" → "October 2026" (or "Oct" when short). */
function monthLabel(month: string, short = false): string {
  const [year, monthNumber] = month.split('-').map(Number);
  return (short ? SHORT_MONTH : MONTH_NAME).format(new Date(year, monthNumber - 1, 1));
}

/**
 * The chart of accounts: every category with what was spent under it in a month, against its budget. Categories
 * are added and edited in place; archived ones are listed apart and can be brought back. The month lives in the URL
 * (?month=2026-09), so a view can be bookmarked.
 */
@Component({
  selector: 'app-categories-page',
  imports: [RouterLink, CurrencyPipe, DecimalPipe, CategoryEditor],
  templateUrl: './categories-page.html',
  styleUrl: './categories-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoriesPage {
  private readonly api = inject(CategoriesApi);

  /** Query parameter "yyyy-MM"; anything else means the current month. */
  readonly month = input<string, string | undefined>(monthOf(new Date()), {
    transform: (value) => (value && /^\d{4}-(0[1-9]|1[0-2])$/.test(value) ? value : monthOf(new Date())),
  });

  protected readonly currentMonth = monthOf(new Date());
  protected readonly previousMonth = computed(() => shiftMonth(this.month(), -1));
  protected readonly nextMonth = computed(() => shiftMonth(this.month(), 1));
  /** No point paging into months that haven't happened. */
  protected readonly canGoForward = computed(() => this.month() < this.currentMonth);
  protected readonly monthName = computed(() => monthLabel(this.month()));
  protected readonly label = monthLabel;

  protected readonly data = httpResource<CategoryMonth>(() => ({
    url: this.api.spendingUrl,
    params: { month: this.month() },
  }));

  protected readonly active = computed(() => (this.data.hasValue() ? this.data.value().categories.filter((c) => !c.isArchived) : []));
  protected readonly archived = computed(() => (this.data.hasValue() ? this.data.value().categories.filter((c) => c.isArchived) : []));

  /** Budget left over; negative when the month went over. */
  protected readonly remaining = computed(() =>
    this.data.hasValue() ? this.data.value().totalBudget - this.data.value().totalSpent : 0,
  );

  protected readonly errorMessage = computed(() => (this.data.error() ? describeHttpError(this.data.error()) : null));

  /** The row being edited: a category's id, 'new' for the add row, or null. One at a time. */
  protected readonly editing = signal<string | null>(null);

  protected readonly unarchiving = signal<string | null>(null);
  protected readonly unarchiveError = signal<string | null>(null);

  /** Share of the budget used, or null when the category has none. */
  protected used(category: CategorySpending): number | null {
    return category.monthlyBudget ? (category.spent / category.monthlyBudget) * 100 : null;
  }

  /** Over budget is rust, nearly there (90%+) is mustard, otherwise ink, as on a printed statement. */
  protected tone(used: number | null): 'over' | 'near' | 'fine' {
    return used === null || used < 90 ? 'fine' : used > 100 ? 'over' : 'near';
  }

  protected onSaved(): void {
    this.editing.set(null);
    this.data.reload();
  }

  /** Brings an archived category back into the pickers and AI's choices, as it was. */
  protected async unarchive(category: CategorySpending): Promise<void> {
    this.unarchiving.set(category.id);
    this.unarchiveError.set(null);
    try {
      await firstValueFrom(
        this.api.update(category.id, {
          name: category.name,
          color: category.color,
          monthlyBudget: category.monthlyBudget,
          isArchived: false,
        }),
      );
      this.data.reload();
    } catch (error) {
      this.unarchiveError.set(describeHttpError(error));
    } finally {
      this.unarchiving.set(null);
    }
  }
}
