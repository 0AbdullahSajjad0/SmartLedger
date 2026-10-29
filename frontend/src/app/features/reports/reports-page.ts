import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { CategorySpending } from '../../core/categories/category.models';
import { describeHttpError } from '../../core/http/describe-http-error';
import { MonthReport } from '../../core/reports/report.models';
import { ReportsApi } from '../../core/reports/reports-api';
import { saveFile } from '../../shared/files/save-file';
import { isMonth, monthLabel, monthOf, shiftMonth } from '../../shared/formatting/months';
import { TrendChart } from './trend-chart/trend-chart';

/** How many months the period picker offers, counting back from this one. */
const MONTHS_OFFERED = 12;

/** A piece of the lede sentence; amounts carry a tone so they can be set in bold and coloured. */
interface LedePart {
  text: string;
  tone?: 'figure' | 'under' | 'over';
}

/**
 * A month in review: a one-sentence lede, AI's written analysis (on request), six months of spending, the top
 * vendors and budgets against actual spending, plus a CSV of the month's receipts. Approved receipts only.
 */
@Component({
  selector: 'app-reports-page',
  imports: [RouterLink, DatePipe, DecimalPipe, TrendChart],
  templateUrl: './reports-page.html',
  styleUrl: './reports-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportsPage {
  private readonly api = inject(ReportsApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Query parameter "yyyy-MM"; anything else means the current month. */
  readonly month = input<string, string | undefined>(monthOf(new Date()), {
    transform: (value) => (isMonth(value) ? value : monthOf(new Date())),
  });

  protected readonly currentMonth = monthOf(new Date());
  protected readonly monthsOffered = Array.from({ length: MONTHS_OFFERED }, (_, i) => shiftMonth(this.currentMonth, -i));
  protected readonly previousMonth = computed(() => shiftMonth(this.month(), -1));
  protected readonly label = monthLabel;

  protected readonly data = httpResource<MonthReport>(() => ({ url: this.api.url, params: { month: this.month() } }));
  protected readonly errorMessage = computed(() => (this.data.error() ? describeHttpError(this.data.error()) : null));

  /** Change on the previous month, as a whole percentage; null when there's nothing to compare with. */
  protected readonly change = computed(() => {
    if (!this.data.hasValue() || this.data.value().previousMonthSpent === 0) {
      return null;
    }
    const { spent, previousMonthSpent } = this.data.value();
    return Math.round(((spent - previousMonthSpent) / previousMonthSpent) * 100);
  });

  /** Budget left over for the month; negative when it went over. Null without budgets. */
  protected readonly remaining = computed(() => {
    if (!this.data.hasValue() || this.data.value().totalBudget === 0) {
      return null;
    }
    return this.data.value().totalBudget - this.data.value().spent;
  });

  /**
   * "You spent $4,286.40, 12% more than September, and closed the month $213.60 inside your overall budget." Built
   * as pieces so the amounts can be emphasised; spacing and punctuation live in the text, not the template.
   */
  protected readonly lede = computed<LedePart[]>(() => {
    if (!this.data.hasValue() || this.data.value().approvedCount === 0) {
      return [];
    }
    const report = this.data.value();
    const money = new Intl.NumberFormat('en', { style: 'currency', currency: report.baseCurrency, currencyDisplay: 'narrowSymbol' });
    const parts: LedePart[] = [{ text: 'You spent ' }, { text: money.format(report.spent), tone: 'figure' }];

    const change = this.change();
    const previous = monthLabel(this.previousMonth(), 'name');
    if (change !== null) {
      parts.push({ text: change === 0 ? `, the same as ${previous}` : `, ${Math.abs(change)}% ${change > 0 ? 'more' : 'less'} than ${previous}` });
    }

    const left = this.remaining();
    if (left === null) {
      parts.push({ text: '.' });
    } else if (left >= 0) {
      parts.push({ text: ', and closed the month ' }, { text: money.format(left), tone: 'under' }, { text: ' inside your overall budget.' });
    } else {
      parts.push({ text: ', and went ' }, { text: money.format(-left), tone: 'over' }, { text: ' over your overall budget.' });
    }
    return parts;
  });

  /** Categories worth a line in "budget against actual": those with a budget or with spending. */
  protected readonly budgetRows = computed(() =>
    this.data.hasValue() ? this.data.value().categories.filter((c) => c.monthlyBudget !== null || c.spent > 0) : [],
  );

  protected readonly writing = signal(false);
  protected readonly insightError = signal<string | null>(null);
  protected readonly exporting = signal(false);
  protected readonly exportError = signal<string | null>(null);

  protected variance(category: CategorySpending): number | null {
    return category.monthlyBudget === null ? null : category.spent - category.monthlyBudget;
  }

  protected openMonth(month: string): void {
    void this.router.navigate([], { queryParams: { month: month === this.currentMonth ? null : month } });
  }

  /** Asks AI to write (or rewrite) the month's analysis, then shows it. */
  protected writeInsight(): void {
    if (this.writing()) {
      return;
    }
    this.writing.set(true);
    this.insightError.set(null);
    this.api
      .writeInsight(this.month())
      .pipe(
        finalize(() => this.writing.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (insight) => this.data.update((report) => (report ? { ...report, insight } : report)),
        error: (error: unknown) => this.insightError.set(describeHttpError(error)),
      });
  }

  protected exportCsv(): void {
    if (this.exporting()) {
      return;
    }
    const month = this.month();
    this.exporting.set(true);
    this.exportError.set(null);
    this.api
      .exportCsv(month)
      .pipe(
        finalize(() => this.exporting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (csv) => saveFile(csv, `smartledger-${month}.csv`),
        error: (error: unknown) => this.exportError.set(describeHttpError(error)),
      });
  }
}
