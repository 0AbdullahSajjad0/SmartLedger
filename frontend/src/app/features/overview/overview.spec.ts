import { HttpRequest } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TEST_API, provideAuthTesting } from '../../core/auth/testing';
import { CategorySpending } from '../../core/categories/category.models';
import { Overview as Statement } from '../../core/overview/overview.models';
import { ReceiptSummary } from '../../core/receipts/receipt.models';
import { monthOf } from '../../shared/formatting/months';
import { Overview } from './overview';

function category(changes: Partial<CategorySpending>): CategorySpending {
  return { id: 'cat', name: 'Category', color: '#3D4A2F', monthlyBudget: null, isArchived: false, receiptCount: 1, spent: 0, ...changes };
}

function receipt(changes: Partial<ReceiptSummary>): ReceiptSummary {
  return {
    id: 'r', number: 1, status: 'Approved', vendorName: 'Vendor', receiptDate: '2026-10-21', currency: 'USD', total: 10,
    baseCurrencyTotal: 10, categoryId: null, fileName: 'r.jpg', createdAt: '2026-10-21T09:00:00Z', ...changes,
  };
}

const OCTOBER: Statement = {
  month: '2026-10',
  baseCurrency: 'USD',
  spent: 4286.4,
  approvedCount: 27,
  totalBudget: 2200,
  previousMonthSpent: 3827,
  awaitingReview: 3,
  unreadable: 1,
  totalReceipts: 31,
  categories: [
    category({ id: 'travel', name: 'Travel', monthlyBudget: 1500, spent: 1640 }),
    category({ id: 'software', name: 'Software & Subscriptions', monthlyBudget: 700, spent: 689 }),
    category({ id: 'food', name: 'Food & Dining', spent: 512.3 }),
  ],
  recent: [
    receipt({ id: 'r3', number: 427, vendorName: 'Skyway Airlines', total: 482, status: 'NeedsReview', categoryId: 'travel' }),
    receipt({ id: 'r2', number: 426, vendorName: null, fileName: 'IMG_2041.jpg', total: null, status: 'Processing', receiptDate: null }),
  ],
  insightHeadline: null,
};

describe('Overview', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<Overview>;
  let page: HTMLElement;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [Overview], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const isOverview = (month: string) => (r: HttpRequest<unknown>) =>
    r.url === `${TEST_API}/api/overview` && r.params.get('month') === month;

  async function render(statement: Statement = OCTOBER, month: string | undefined = '2026-10') {
    fixture = TestBed.createComponent(Overview);
    fixture.componentRef.setInput('month', month);
    page = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    (await vi.waitFor(() => http.expectOne(isOverview(statement.month)))).flush(statement);
    http.expectOne(`${TEST_API}/api/categories`).flush([
      { id: 'travel', name: 'Travel', color: '#3D4A2F', monthlyBudget: 1500, isArchived: false },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Each key figure as "label: value · note". */
  function figures(): string[] {
    return [...page.querySelectorAll('.figure')].map((figure) => {
      const text = (selector: string) => [...figure.querySelectorAll(selector)].map((e) => e.textContent!.replace(/\s+/g, ' ').trim());
      return `${text('.label')[0]}: ${text('.figure__value')[0]} · ${text('.figure__note').join(' · ')}`;
    });
  }

  it('welcomes a brand-new account instead of showing an empty statement', async () => {
    await render({ ...OCTOBER, totalReceipts: 0, spent: 0, approvedCount: 0, categories: [], recent: [] });

    expect(page.querySelector('h1')?.textContent).toContain('Welcome');
    expect(page.querySelector('a[href="/scan"]')).not.toBeNull();
    expect(page.querySelector('.figures')).toBeNull();
  });

  it('shows the month’s key figures', async () => {
    // A past month, so the labels don't depend on today's date.
    await render({ ...OCTOBER, month: '2025-03' }, '2025-03');

    expect(page.querySelector('h1')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('March 2025');
    expect(figures()).toEqual([
      'Spent in March: $4,286.40 · ▲ 12.0% on February ($3,827.00)',
      'Awaiting review: 3 · Review entries → · + 1 unreadable',
      'Average receipt: $158.76 · 27 receipts',
      'Over budget: 1 · Travel, +$140.00',
    ]);
    expect(page.querySelector('.figure__note--up')).not.toBeNull();
    expect(page.querySelector('a.figure--link')?.getAttribute('href')).toBe('/receipts?status=NeedsReview');
  });

  it('calls the current month "this month"', async () => {
    const thisMonth = monthOf(new Date());
    await render({ ...OCTOBER, month: thisMonth }, undefined);

    expect(figures()[0]).toMatch(/^Spent this month: /);
  });

  it('marks a month that cost less than the last one in green', async () => {
    await render({ ...OCTOBER, spent: 3000 });

    expect(page.querySelector('.figure__note--down')?.textContent).toContain('▼ 21.6% on September');
  });

  it("says when there's no month before to compare with, and no budgets to judge by", async () => {
    await render({
      ...OCTOBER,
      previousMonthSpent: 0,
      totalBudget: 0,
      categories: [category({ id: 'food', name: 'Food & Dining', spent: 40 })],
    });

    expect(figures()[0]).toContain('Nothing approved in September');
    expect(figures()[3]).toBe('Over budget: — · Set budgets →');
  });

  it('knows about budgets even in a month with nothing spent', async () => {
    await render({ ...OCTOBER, month: '2026-09', spent: 0, approvedCount: 0, previousMonthSpent: 0, categories: [] }, '2026-09');

    expect(figures()[3]).toBe('Over budget: 0 · All within budget');
  });

  it('draws each category against the biggest one, with its share of the month', async () => {
    await render();

    const rows = [...page.querySelectorAll('.bar-row')].map((row) => ({
      text: [...row.querySelectorAll('span')].map((s) => s.textContent!.trim()).filter(Boolean).join(' | '),
      width: (row.querySelector('.bar-row__bar') as HTMLElement).style.width,
    }));
    expect(rows).toEqual([
      { text: 'Travel | 1,640.00 | 38.3%', width: '100%' },
      { text: 'Software & Subscriptions | 689.00 | 16.1%', width: `${(689 / 1640) * 100}%` },
      { text: 'Food & Dining | 512.30 | 12.0%', width: `${(512.3 / 1640) * 100}%` },
    ]);
  });

  it('sums the month up in plain sentences', async () => {
    await render();

    const lines = [...page.querySelectorAll('.summary p')].map((p) => p.textContent!.trim());
    expect(lines).toEqual([
      'Travel ran $140.00 over budget. Everything else stayed in line.',
      'Travel took the largest share: $1,640.00, 38% of the month.',
      'Spending was 12.0% higher than in September.',
    ]);
  });

  it('leads the summary with AI’s headline once the report has one', async () => {
    await render({ ...OCTOBER, insightHeadline: 'Travel ran $140.00 over budget; everything else held.' });

    expect(page.querySelector('#summary-title')?.textContent).toContain('AI summary');
    expect(page.querySelector('.summary__lede')?.textContent).toBe('Travel ran $140.00 over budget; everything else held.');
    expect(page.querySelector('.summary__link')?.textContent?.trim()).toBe('Read the full report →');
  });

  it('lists the newest entries with their category and status', async () => {
    await render();

    const rows = [...page.querySelectorAll('.recent tbody tr')].map((row) =>
      [...row.querySelectorAll('td')].map((cell) => cell.textContent!.replace(/\s+/g, ' ').trim()).join(' | '),
    );
    expect(rows[0]).toBe('0427 | 21 OCT 2026 | Skyway Airlines | Travel | 482.00 USD | Needs review');
    expect(rows[1]).toBe('0426 | — | IMG_2041.jpg | — | — | Reading');
  });

  it("says when nothing's been approved in the month yet", async () => {
    await render({ ...OCTOBER, month: '2026-09', spent: 0, approvedCount: 0, previousMonthSpent: 0, categories: [] }, '2026-09');

    expect(page.querySelector('.by-category__empty')?.textContent).toContain('Nothing approved in September yet.');
    expect(page.querySelector('.summary p')?.textContent).toContain('Approve receipts to see how September is going.');
  });

  it('opens another month from the picker', async () => {
    await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    const picker = page.querySelector<HTMLSelectElement>('#statement-month')!;
    picker.value = picker.options[1].value;
    picker.dispatchEvent(new Event('change'));

    expect(navigate).toHaveBeenCalledWith([], { queryParams: { month: picker.options[1].value } });
  });
});
