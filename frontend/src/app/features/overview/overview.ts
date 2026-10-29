import { CurrencyPipe, DatePipe, DecimalPipe, UpperCasePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/auth/auth-store';
import { CategorySpending } from '../../core/categories/category.models';
import { describeHttpError } from '../../core/http/describe-http-error';
import { Overview as Statement } from '../../core/overview/overview.models';
import { OverviewApi } from '../../core/overview/overview-api';
import { Category } from '../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../core/receipts/receipts-api';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { isMonth, monthLabel, monthOf, shiftMonth } from '../../shared/formatting/months';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';

/** How many months the statement picker offers, counting back from this one. */
const MONTHS_OFFERED = 12;

const MONEY = (currency: string) =>
  new Intl.NumberFormat('en', { style: 'currency', currency, currencyDisplay: 'narrowSymbol' });

/**
 * The signed-in home page: the month's statement. What was spent and how it compares with last month, what's waiting
 * for review, how each category is doing against its budget, and the newest entries. A brand-new account gets a
 * welcome instead. The month lives in the URL (?month=2026-09).
 */
@Component({
  selector: 'app-overview',
  imports: [RouterLink, CurrencyPipe, DatePipe, DecimalPipe, UpperCasePipe, EntryNumberPipe, StatusStamp],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Overview {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly overviewApi = inject(OverviewApi);
  private readonly receiptsApi = inject(ReceiptsApi);

  /** Query parameter "yyyy-MM"; anything else means the current month. */
  readonly month = input<string, string | undefined>(monthOf(new Date()), {
    transform: (value) => (isMonth(value) ? value : monthOf(new Date())),
  });

  protected readonly user = this.auth.user;
  protected readonly firstName = computed(() => this.user()?.displayName.split(' ')[0] ?? '');

  protected readonly currentMonth = monthOf(new Date());
  protected readonly isCurrentMonth = computed(() => this.month() === this.currentMonth);
  protected readonly label = monthLabel;

  /** This month and the eleven before it, newest first. */
  protected readonly monthsOffered = Array.from({ length: MONTHS_OFFERED }, (_, i) => shiftMonth(this.currentMonth, -i));

  protected readonly data = httpResource<Statement>(() => ({ url: this.overviewApi.url, params: { month: this.month() } }));

  /** For the category names in the recent entries. */
  private readonly categories = httpResource<Category[]>(() => this.receiptsApi.categoriesUrl, { defaultValue: [] });

  protected readonly errorMessage = computed(() => (this.data.error() ? describeHttpError(this.data.error()) : null));

  protected readonly previousMonth = computed(() => shiftMonth(this.month(), -1));

  /** Change on last month as a percentage; null when last month had nothing to compare with. */
  protected readonly change = computed(() => {
    if (!this.data.hasValue() || this.data.value().previousMonthSpent === 0) {
      return null;
    }
    const { spent, previousMonthSpent } = this.data.value();
    return ((spent - previousMonthSpent) / previousMonthSpent) * 100;
  });

  protected readonly average = computed(() => {
    const statement = this.data.hasValue() ? this.data.value() : null;
    return statement && statement.approvedCount > 0 ? statement.spent / statement.approvedCount : null;
  });

  /** Categories that went over their budget, worst first. */
  protected readonly overBudget = computed(() =>
    (this.data.hasValue() ? this.data.value().categories : [])
      .filter((c) => c.monthlyBudget !== null && c.spent > c.monthlyBudget)
      .map((c) => ({ ...c, over: c.spent - c.monthlyBudget! }))
      .sort((a, b) => b.over - a.over),
  );

  protected readonly hasBudgets = computed(() => this.data.hasValue() && this.data.value().totalBudget > 0);

  /** Bars are drawn against the biggest category, so it fills the row. */
  private readonly largest = computed(() => Math.max(0, ...(this.data.hasValue() ? this.data.value().categories.map((c) => c.spent) : [])));

  protected barWidth(category: CategorySpending): number {
    return this.largest() > 0 ? (category.spent / this.largest()) * 100 : 0;
  }

  protected share(category: CategorySpending): number {
    const spent = this.data.hasValue() ? this.data.value().spent : 0;
    return spent > 0 ? (category.spent / spent) * 100 : 0;
  }

  protected categoryName(id: string | null): string | null {
    return id ? (this.categories.value().find((c) => c.id === id)?.name ?? null) : null;
  }

  /**
   * A few plain sentences about the month, worked out from the figures: budgets broken or kept, where most of the
   * money went, and how it compares with last month.
   */
  protected readonly summary = computed(() => {
    if (!this.data.hasValue() || this.data.value().approvedCount === 0) {
      return [];
    }
    const statement = this.data.value();
    const money = MONEY(statement.baseCurrency);
    const lines: string[] = [];

    const over = this.overBudget();
    if (over.length === 1) {
      // Other budgets exist when the total is more than the overspent category's own.
      const othersBudgeted = statement.totalBudget > (over[0].monthlyBudget ?? 0);
      lines.push(`${over[0].name} ran ${money.format(over[0].over)} over budget.${othersBudgeted ? ' Everything else stayed in line.' : ''}`);
    } else if (over.length > 1) {
      lines.push(`${over.length} categories went over budget, ${over[0].name} by the most (${money.format(over[0].over)}).`);
    } else if (this.hasBudgets()) {
      lines.push('Every category stayed within its budget.');
    }

    const top = statement.categories[0];
    if (top && statement.categories.length > 1) {
      lines.push(`${top.name} took the largest share: ${money.format(top.spent)}, ${Math.round(this.share(top))}% of the month.`);
    }

    const change = this.change();
    if (change !== null && Math.abs(change) >= 1) {
      lines.push(
        `Spending was ${Math.abs(change).toFixed(1)}% ${change > 0 ? 'higher' : 'lower'} than in ${monthLabel(this.previousMonth(), 'name')}.`,
      );
    }

    return lines;
  });

  protected openMonth(month: string): void {
    void this.router.navigate([], { queryParams: { month: month === this.currentMonth ? null : month } });
  }
}
