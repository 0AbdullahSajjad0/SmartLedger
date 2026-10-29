import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TEST_API, provideAuthTesting } from '../../core/auth/testing';
import { ReceiptDetail } from '../../core/receipts/receipt.models';
import { ReceiptPage } from './receipt-page';
import { receiptDetail } from './receipt-testing';

const RECEIPT_URL = `${TEST_API}/api/receipts/r-1`;

describe('ReceiptPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    // jsdom has no blob: URLs; the page only needs an address string back.
    URL.createObjectURL = vi.fn(() => 'blob:test-receipt');
    URL.revokeObjectURL = vi.fn();
    TestBed.configureTestingModule({ imports: [ReceiptPage], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Renders the page; `image` is which picture it's expected to fetch once the details arrive. */
  async function render(receipt: ReceiptDetail | null, image: 'preview' | 'file' | null = null) {
    const fixture = TestBed.createComponent(ReceiptPage);
    fixture.componentRef.setInput('id', 'r-1');
    fixture.detectChanges();

    const details = await vi.waitFor(() => http.expectOne(RECEIPT_URL));
    http.expectOne(`${TEST_API}/api/categories`).flush([
      { id: 'cat-office', name: 'Office Supplies', color: '#8C8270', monthlyBudget: null, isArchived: false },
    ]);

    if (receipt) {
      details.flush(receipt);
    } else {
      details.flush({ title: 'Receipt not found.' }, { status: 404, statusText: 'Not Found' });
    }

    // Answered before whenStable(), which would otherwise wait for this request forever.
    if (image) {
      const request = await vi.waitFor(() => http.expectOne(`${RECEIPT_URL}/${image}`));
      request.flush(new Blob(['jpeg'], { type: 'image/jpeg' }));
    }

    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  it('shows the cleaned preview next to the record', async () => {
    const { page } = await render(receiptDetail(), 'preview');

    expect(page.querySelector('img')?.getAttribute('src')).toBe('blob:test-receipt');
    expect(page.textContent).toContain('Entry 0001');
    expect(page.textContent).toContain('including any location your camera saved in it');
  });

  it("says the preview is being made while it isn't ready yet", async () => {
    const { page } = await render(receiptDetail({ status: 'Processing', hasPreview: false }));

    expect(page.querySelector('img')).toBeNull();
    expect(page.textContent).toContain('PREPARING THE PREVIEW');
    expect(page.textContent).toContain('AI is reading this receipt');
    expect(page.querySelector('app-receipt-review-form')).toBeNull();
  });

  it("puts what AI read into the review form, marking the fields it wasn't sure about", async () => {
    const { page } = await render(
      receiptDetail({
        vendorName: 'Paper & Co. Office Supply',
        paymentMethod: 'VISA ····4242',
        categoryId: 'cat-office',
        categorySuggestedByAi: true,
        total: 126.45,
        currency: 'USD',
        extractionFlags: [{ field: 'PaymentMethod', reason: 'One digit of the card number was faded.' }],
      }),
      'preview',
    );

    expect(page.querySelector('h1')?.textContent).toContain('Paper & Co. Office Supply');
    expect(page.querySelector<HTMLInputElement>('#vendorName')?.value).toBe('Paper & Co. Office Supply');
    expect(page.querySelector<HTMLSelectElement>('#categoryId')?.selectedOptions[0].textContent).toContain('Office Supplies');
    expect(page.textContent).toContain('suggested');
    expect(page.querySelector('.field--flagged')?.textContent).toContain('Paid with');
    expect(page.querySelector('.footnotes')?.textContent).toContain('One digit of the card number was faded.');
  });

  it("offers to fill in a receipt AI couldn't read", async () => {
    const { page } = await render(receiptDetail({ status: 'Failed', failureReason: 'Too blurry to read.' }), 'preview');

    expect(page.textContent).toContain('Too blurry to read. You can fill in the details yourself, or reject it.');
    expect(page.querySelector('app-receipt-review-form')).not.toBeNull();
  });

  it('shows an approved receipt as posted, with the rate it was converted at, and can reopen it', async () => {
    const approved = receiptDetail({
      status: 'Approved',
      vendorName: 'Lahore Chai House',
      currency: 'PKR',
      total: 2800,
      baseCurrency: 'EUR',
      exchangeRate: 0.0032142857,
      exchangeRateDate: '2026-10-15',
      baseCurrencyTotal: 9,
      approvedAt: '2026-10-28T10:00:00Z',
    });
    const { fixture, page } = await render(approved, 'preview');

    expect(page.querySelector('app-receipt-review-form')).toBeNull();
    expect(page.querySelector('.posted__amount')?.textContent).toContain('EUR9.00');
    expect(page.querySelector('.posted__basis')?.textContent).toContain('1 PKR = 0.0032142857 EUR');
    expect(page.querySelector('.posted__basis')?.textContent).toContain('15 Oct 2026');

    page.querySelector<HTMLButtonElement>('.posted button')!.click();
    const reopen = await vi.waitFor(() => http.expectOne({ method: 'POST', url: `${RECEIPT_URL}/reopen` }));
    reopen.flush({ ...approved, status: 'NeedsReview', baseCurrencyTotal: null, approvedAt: null });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page.querySelector('app-receipt-review-form')).not.toBeNull();
  });

  it('shows the original for receipts uploaded before previews existed, if the browser can display it', async () => {
    const { page } = await render(
      receiptDetail({ status: 'Approved', hasPreview: false, fileName: 'old.png', contentType: 'image/png' }),
      'file',
    );

    expect(page.querySelector('img')?.getAttribute('src')).toBe('blob:test-receipt');
  });

  it('offers only the download for old files the browser cannot display', async () => {
    const { page } = await render(receiptDetail({ status: 'Approved', hasPreview: false }));

    expect(page.querySelector('img')).toBeNull();
    expect(page.textContent).toContain("There's no preview of this file");
  });

  it('downloads the original only when asked, named by entry number', async () => {
    const { fixture, page } = await render(
      receiptDetail({ number: 3, fileName: 'invoice.PDF', contentType: 'application/pdf' }),
      'preview',
    );
    let savedAs: string | null = null;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      savedAs = this.download;
    });

    expect(page.querySelector('.download__note')?.textContent?.trim()).toBe('Exactly as uploaded.');
    page.querySelector<HTMLButtonElement>('.download button')!.click();
    // The auth interceptor adds the token asynchronously, so the request appears a moment after the click.
    const original = await vi.waitFor(() => http.expectOne(`${RECEIPT_URL}/file`));
    original.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    await fixture.whenStable();

    expect(savedAs).toBe('receipt-0003.pdf');
  });

  it('deletes a receipt permanently, after saying what will be lost', async () => {
    const { fixture, page } = await render(receiptDetail({ status: 'Approved', currency: 'USD', baseCurrency: 'USD' }), 'preview');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const button = (label: string) => [...page.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;

    button('Delete receipt').click();
    fixture.detectChanges();
    expect(page.querySelector('.danger__question')?.textContent).toContain("can't be recovered. It also leaves your reports.");

    button('Delete permanently').click();
    const deletion = await vi.waitFor(() => http.expectOne({ method: 'DELETE', url: RECEIPT_URL }));
    deletion.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith('/receipts');
  });

  it('offers no delete while AI is still reading the receipt', async () => {
    const { page } = await render(receiptDetail({ status: 'Processing', hasPreview: false }));

    expect([...page.querySelectorAll('button')].some((b) => b.textContent?.includes('Delete receipt'))).toBe(false);
  });

  it('says so when the receipt does not exist or is not yours', async () => {
    const { page } = await render(null);

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Receipt not found.');
  });
});
