import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../../core/auth/testing';
import { Category, ReceiptDetail } from '../../../core/receipts/receipt.models';
import { receiptDetail } from '../receipt-testing';
import { ReceiptReviewForm } from './review-form';

const RECEIPT_URL = `${TEST_API}/api/receipts/r-1`;

const CATEGORIES: Category[] = [
  { id: 'cat-food', name: 'Food & Dining', color: '#8C8270', monthlyBudget: null, isArchived: false },
  { id: 'cat-old', name: 'Old category', color: '#8C8270', monthlyBudget: null, isArchived: true },
];

/** Read by AI and complete: ready to approve as it is. */
const READ = receiptDetail({
  vendorName: 'Lahore Chai House',
  receiptDate: '2026-10-15',
  currency: 'EUR',
  subtotal: 40,
  tax: 5,
  total: 45,
  categoryId: 'cat-food',
  categorySuggestedByAi: true,
  lineItems: [
    { lineNumber: 1, description: 'Chai', quantity: 2, unitPrice: 10, amount: 20 },
    { lineNumber: 2, description: 'Samosa', quantity: 4, unitPrice: 5, amount: 20 },
  ],
});

describe('ReceiptReviewForm', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ReceiptReviewForm>;
  let emitted: ReceiptDetail[];

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [ReceiptReviewForm], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(receipt: ReceiptDetail, baseCurrency = 'EUR') {
    fixture = TestBed.createComponent(ReceiptReviewForm);
    fixture.componentRef.setInput('receipt', receipt);
    fixture.componentRef.setInput('categories', CATEGORIES);
    fixture.componentRef.setInput('baseCurrency', baseCurrency);
    emitted = [];
    fixture.componentInstance.changed.subscribe((value) => emitted.push(value));
    // No whenStable() here: when the form looks up a rate, it would wait for that request, which the test answers.
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(form: HTMLElement, selector: string, value: string): void {
    const input = form.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function click(form: HTMLElement, label: string): void {
    const button = [...form.querySelectorAll('button')].find((b) => b.textContent?.trim() === label);
    button!.click();
  }

  it('fills the form with what AI read, offering only active categories', async () => {
    const form = await render(READ);

    expect(form.querySelector<HTMLInputElement>('#vendorName')?.value).toBe('Lahore Chai House');
    expect(form.querySelector<HTMLInputElement>('#receiptDate')?.value).toBe('2026-10-15');
    expect(form.querySelectorAll('tbody tr')).toHaveLength(2);
    expect([...form.querySelectorAll('#categoryId option')].map((o) => o.textContent?.trim())).toEqual([
      'Choose a category',
      'Food & Dining',
    ]);
  });

  it('saves corrections first, then approves', async () => {
    const form = await render(READ);
    type(form, '#vendorName', 'Lahore Chai House & Bakery');

    click(form, 'Approve');
    const put = await vi.waitFor(() => http.expectOne({ method: 'PUT', url: RECEIPT_URL }));
    expect(put.request.body.vendorName).toBe('Lahore Chai House & Bakery');
    expect(put.request.body.lineItems).toEqual([
      { description: 'Chai', quantity: 2, unitPrice: 10, amount: 20 },
      { description: 'Samosa', quantity: 4, unitPrice: 5, amount: 20 },
    ]);
    put.flush({ ...READ, vendorName: 'Lahore Chai House & Bakery' });

    const approve = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${RECEIPT_URL}/approve` }));
    expect(approve.request.body).toEqual({ amountCharged: null });
    approve.flush({ ...READ, status: 'Approved' });
    await settle();

    expect(emitted.at(-1)?.status).toBe('Approved');
  });

  it("approves straight away when nothing changed, sending the amount charged", async () => {
    const form = await render(READ);
    type(form, '#amountCharged', '46.10');

    click(form, 'Approve');
    const approve = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${RECEIPT_URL}/approve` }));

    expect(approve.request.body).toEqual({ amountCharged: 46.1 });
    approve.flush({ ...READ, status: 'Approved' });
    await settle();
    expect(form.querySelector('.post__basis')?.textContent).toContain('From your statement');
  });

  it('says what approving still needs, without asking the API', async () => {
    const form = await render({ ...READ, categoryId: null, vendorName: null });

    click(form, 'Approve');
    await settle();

    expect(form.querySelector('[role="alert"]')?.textContent).toContain('Add the vendor and category before approving.');
  });

  it("warns when the line items don't add up, without blocking", async () => {
    const form = await render(READ);

    type(form, 'tbody tr:first-child input[aria-label="Line 1 amount"]', '25');
    await settle();

    expect(form.querySelector('.checks')?.textContent).toContain('The line items add up to 45.00, but the subtotal is 40.00.');
  });

  it('works out a line amount from quantity and unit price', async () => {
    const form = await render(READ);

    type(form, 'input[aria-label="Line 1 quantity"]', '3');
    await settle();

    expect(form.querySelector<HTMLInputElement>('input[aria-label="Line 1 amount"]')?.value).toBe('30');
  });

  it('adds and removes lines', async () => {
    const form = await render(READ);

    click(form, '+ Add a line');
    await settle();
    expect(form.querySelectorAll('tbody tr')).toHaveLength(3);

    form.querySelector<HTMLButtonElement>('button[aria-label="Remove line 1"]')!.click();
    await settle();
    expect(form.querySelector<HTMLInputElement>('input[aria-label="Line 1 description"]')?.value).toBe('Samosa');
  });

  it('previews the amount in the base currency at the receipt date’s rate', async () => {
    const form = await render({ ...READ, currency: 'PKR', subtotal: null, tax: null, total: 2800, lineItems: [] });

    const rate = await vi.waitFor(() => http.expectOne((r) => r.url === `${TEST_API}/api/exchange-rates`));
    expect(rate.request.params.get('from')).toBe('PKR');
    expect(rate.request.params.get('to')).toBe('EUR');
    expect(rate.request.params.get('date')).toBe('2026-10-15');
    rate.flush({ from: 'PKR', to: 'EUR', rate: 0.0032142857, rateDate: '2026-10-14' });
    await settle();

    expect(form.querySelector('.post__amount')?.textContent).toContain('9.00');
    expect(form.querySelector('.post__basis')?.textContent).toContain('1 PKR = 0.00321429 EUR');
    expect(form.querySelector('.post__basis')?.textContent).toContain('(latest available)');
  });

  it("doesn't look the rate up again while other fields are typed", async () => {
    const form = await render({ ...READ, currency: 'PKR' });
    const rate = await vi.waitFor(() => http.expectOne((r) => r.url === `${TEST_API}/api/exchange-rates`));
    rate.flush({ from: 'PKR', to: 'EUR', rate: 0.0032, rateDate: '2026-10-15' });

    type(form, '#vendorName', 'Somewhere else');
    type(form, '#notes', 'Team breakfast');
    await settle();

    http.expectNone((r) => r.url === `${TEST_API}/api/exchange-rates`);
  });

  it('rejects', async () => {
    const form = await render(READ);

    click(form, 'Reject');
    const reject = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${RECEIPT_URL}/reject` }));
    reject.flush({ ...READ, status: 'Rejected' });
    await settle();

    expect(emitted.at(-1)?.status).toBe('Rejected');
  });

  it("shows the API's reason when approving fails", async () => {
    const form = await render(READ);

    click(form, 'Approve');
    const approve = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${RECEIPT_URL}/approve` }));
    approve.flush(
      { title: 'No exchange rate is available for 15 Oct 2026. Enter the amount charged instead.' },
      { status: 400, statusText: 'Bad Request' },
    );

    // The message appears once the failed request's promise has been handled.
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(form.querySelector('[role="alert"]')?.textContent).toContain('Enter the amount charged instead.');
    });
    expect(emitted).toHaveLength(0);
  });
});
