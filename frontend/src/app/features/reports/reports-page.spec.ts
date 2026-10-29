import { HttpRequest } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../core/auth/testing';
import { CategorySpending } from '../../core/categories/category.models';
import { Insight, MonthReport } from '../../core/reports/report.models';
import { ReportsPage } from './reports-page';

const REPORTS_URL = `${TEST_API}/api/reports`;

function category(changes: Partial<CategorySpending>): CategorySpending {
  return { id: 'cat', name: 'Category', color: '#3D4A2F', monthlyBudget: null, isArchived: false, receiptCount: 1, spent: 0, ...changes };
}

const INSIGHT: Insight = {
  headline: 'Travel ran USD 140.00 over budget.',
  paragraphs: ['Two Skyway Airlines flights account for most of it.', 'Food & Dining came in under budget.'],
  receiptCount: 22,
  generatedAt: '2026-10-28T10:00:00Z',
  isOutOfDate: false,
};

const OCTOBER: MonthReport = {
  month: '2026-10',
  baseCurrency: 'USD',
  spent: 4286.4,
  approvedCount: 22,
  previousMonthSpent: 3827,
  totalBudget: 4500,
  trend: [
    { month: '2026-05', spent: 3410 },
    { month: '2026-06', spent: 3920 },
    { month: '2026-07', spent: 3105 },
    { month: '2026-08', spent: 0 },
    { month: '2026-09', spent: 3827 },
    { month: '2026-10', spent: 4286.4 },
  ],
  topVendors: [
    { vendor: 'Skyway Airlines', categoryName: 'Travel', receiptCount: 2, spent: 1120 },
    { vendor: 'Grand Plaza Hotel', categoryName: 'Lodging', receiptCount: 1, spent: 670.15 },
  ],
  categories: [
    category({ id: 'travel', name: 'Travel', monthlyBudget: 1500, spent: 1640 }),
    category({ id: 'software', name: 'Software & Subscriptions', monthlyBudget: 700, spent: 689 }),
    category({ id: 'misc', name: 'Misc', monthlyBudget: null, spent: 0 }),
    category({ id: 'food', name: 'Food & Dining', monthlyBudget: null, spent: 120 }),
  ],
  insight: null,
};

describe('ReportsPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ReportsPage>;
  let page: HTMLElement;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [ReportsPage], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const isReport = (month: string) => (r: HttpRequest<unknown>) => r.url === REPORTS_URL && r.params.get('month') === month;

  async function render(report: MonthReport = OCTOBER) {
    fixture = TestBed.createComponent(ReportsPage);
    fixture.componentRef.setInput('month', report.month);
    page = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne(isReport(report.month)))).flush(report);
    await settle();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = (selector: string) => page.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();

  function button(label: string): HTMLButtonElement {
    return [...page.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
  }

  it('leads with what was spent, against last month and the budget', async () => {
    await render();

    expect(text('h1')).toBe('October in review');
    expect(text('.lede')).toBe('You spent $4,286.40, 12% more than September, and closed the month $213.60 inside your overall budget.');
    expect(page.querySelector('.lede__figure--under')?.textContent).toBe('$213.60');
  });

  it('says so when the month went over the overall budget', async () => {
    await render({ ...OCTOBER, totalBudget: 4000, previousMonthSpent: 0 });

    expect(text('.lede')).toBe('You spent $4,286.40, and went $286.40 over your overall budget.');
  });

  it('offers AI’s analysis on request, and shows it once written', async () => {
    await render();
    expect(text('.analysis__empty')).toContain('never your notes, card numbers or receipt images');

    button('Write the analysis').click();
    const post = await vi.waitFor(() => http.expectOne((r) => r.method === 'POST' && r.url === `${REPORTS_URL}/insight`));
    expect(post.request.params.get('month')).toBe('2026-10');
    post.flush(INSIGHT);
    await settle();

    expect(text('.analysis__headline')).toBe('Travel ran USD 140.00 over budget.');
    expect([...page.querySelectorAll('.analysis__body p')].map((p) => p.textContent)).toEqual(INSIGHT.paragraphs);
    expect(text('.analysis__source')).toBe('Written 28 Oct 2026 from 22 approved receipts · verify figures before relying on them');
    expect(button('Regenerate')).toBeTruthy();
  });

  it('says when the analysis no longer matches the figures', async () => {
    await render({ ...OCTOBER, insight: { ...INSIGHT, isOutOfDate: true } });

    expect(text('.analysis__stale')).toContain('Receipts were approved or reopened since this was written');
  });

  it("shows why the analysis couldn't be written", async () => {
    await render();

    button('Write the analysis').click();
    (await vi.waitFor(() => http.expectOne((r) => r.method === 'POST' && r.url === `${REPORTS_URL}/insight`))).flush(
      { title: 'AI couldn’t write the analysis just now. Try again in a minute.' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(text('.analysis [role="alert"]')).toContain('Try again in a minute');
    });
  });

  it('draws six months with the month under review picked out', async () => {
    await render();

    const columns = [...page.querySelectorAll('.chart__column')];
    expect(columns.map((c) => c.querySelector('.chart__value')?.textContent)).toEqual(['3.4k', '3.9k', '3.1k', '0', '3.8k', '4.3k']);
    expect(columns.map((c) => c.classList.contains('chart__column--selected'))).toEqual([false, false, false, false, false, true]);
    expect(page.querySelector('.chart')?.getAttribute('aria-label')).toContain('October 2026');
    expect((columns[5].querySelector('.chart__bar') as HTMLElement).style.height).toBe('100%');
  });

  it('ranks the top vendors', async () => {
    await render();

    const rows = [...page.querySelectorAll('.vendors__row')].map((r) => r.textContent!.replace(/\s+/g, ' ').trim());
    expect(rows).toEqual(['01 Skyway Airlines Travel · 2 receipts 1,120.00', '02 Grand Plaza Hotel Lodging · 1 receipt 670.15']);
  });

  it('puts budgets against actual spending, with the variance', async () => {
    await render();

    const rows = [...page.querySelectorAll('.budget__table tr')].map((row) =>
      [...row.querySelectorAll('th, td')].map((c) => c.textContent!.replace(/\s+/g, ' ').trim()).join(' | '),
    );
    expect(rows).toEqual([
      'Category | Spent | Budget | Variance',
      'Travel | 1,640.00 | 1,500.00 | +140.00',
      'Software & Subscriptions | 689.00 | 700.00 | −11.00',
      'Food & Dining | 120.00 | — | —',
      'Total | 4,286.40 | 4,500.00 | −213.60',
    ]);
    expect(page.querySelector('.budget__table tbody tr td.over')?.textContent?.trim()).toBe('+140.00');
  });

  it('exports the month’s receipts as CSV', async () => {
    await render();
    let savedAs: string | null = null;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      savedAs = this.download;
    });
    URL.createObjectURL = vi.fn(() => 'blob:csv');
    URL.revokeObjectURL = vi.fn();

    button('Export CSV').click();
    const csv = await vi.waitFor(() => http.expectOne((r) => r.url === `${REPORTS_URL}/export`));
    expect(csv.request.params.get('month')).toBe('2026-10');
    csv.flush(new Blob(['Entry,Date'], { type: 'text/csv' }));
    await settle();

    expect(savedAs).toBe('smartledger-2026-10.csv');
  });

  it('has nothing to analyse or export before anything is approved', async () => {
    await render({ ...OCTOBER, spent: 0, approvedCount: 0, topVendors: [] });

    expect(text('.lede')).toContain('Nothing has been approved for October yet.');
    expect(text('.analysis__empty')).toContain('Approve some from October first.');
    expect(button('Export CSV').disabled).toBe(true);
    expect([...page.querySelectorAll('button')].some((b) => b.textContent?.includes('Write the analysis'))).toBe(false);
  });
});
