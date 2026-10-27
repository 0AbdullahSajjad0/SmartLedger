import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../core/auth/testing';
import { ReceiptSummary } from '../../core/receipts/receipt.models';
import { UploadQueue } from './upload-queue';

const RECEIPTS_URL = `${TEST_API}/api/receipts`;

function receipt(number: number, fileName: string): ReceiptSummary {
  return {
    id: `receipt-${number}`,
    number,
    status: 'Processing',
    vendorName: null,
    receiptDate: null,
    currency: null,
    total: null,
    baseCurrencyTotal: null,
    categoryId: null,
    fileName,
    createdAt: new Date().toISOString(),
  };
}

describe('UploadQueue', () => {
  let queue: UploadQueue;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideAuthTesting(), UploadQueue] });
    queue = TestBed.inject(UploadQueue);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('rejects a file that is not a receipt without sending it', () => {
    queue.add([new File(['hello'], 'notes.txt', { type: 'text/plain' })]);

    expect(queue.items()[0].state).toBe('failed');
    expect(queue.items()[0].error).toContain('Not a receipt image or PDF');
    expect(queue.items()[0].retryable).toBe(false);
    http.expectNone(RECEIPTS_URL);
  });

  it('uploads a receipt and records the new ledger entry', async () => {
    queue.add([new File([new Uint8Array([0xff, 0xd8, 0xff])], 'coffee.jpg', { type: 'image/jpeg' })]);

    const request = await vi.waitFor(() => http.expectOne(RECEIPTS_URL));
    expect(request.request.method).toBe('POST');
    expect((request.request.body as FormData).getAll('files')).toHaveLength(1);
    request.flush([receipt(7, 'coffee.jpg')]);

    await vi.waitFor(() => expect(queue.items()[0].state).toBe('done'));
    expect(queue.items()[0].receipt?.number).toBe(7);
    expect(queue.busy()).toBe(false);
  });

  it("shows the server's reason when an upload is refused, and can retry it", async () => {
    queue.add([new File([new Uint8Array([0xff, 0xd8, 0xff])], 'coffee.jpg', { type: 'image/jpeg' })]);

    (await vi.waitFor(() => http.expectOne(RECEIPTS_URL))).flush(
      { title: 'Too many attempts.' },
      { status: 429, statusText: 'Too Many Requests' },
    );
    await vi.waitFor(() => expect(queue.items()[0].state).toBe('failed'));
    expect(queue.items()[0].error).toBe('Too many attempts. Wait a minute and try again.');
    expect(queue.items()[0].retryable).toBe(true);

    queue.retry(queue.items()[0].key);
    (await vi.waitFor(() => http.expectOne(RECEIPTS_URL))).flush([receipt(8, 'coffee.jpg')]);
    await vi.waitFor(() => expect(queue.items()[0].state).toBe('done'));
  });

  it('uploads several files one after another, each as its own request', async () => {
    const jpeg = (name: string) => new File([new Uint8Array([0xff, 0xd8, 0xff])], name, { type: 'image/jpeg' });
    queue.add([jpeg('a.jpg'), jpeg('b.jpg')]);

    (await vi.waitFor(() => http.expectOne(RECEIPTS_URL))).flush([receipt(1, 'a.jpg')]);
    (await vi.waitFor(() => http.expectOne(RECEIPTS_URL))).flush([receipt(2, 'b.jpg')]);

    await vi.waitFor(() => expect(queue.items().every((item) => item.state === 'done')).toBe(true));
    // Newest first in the intake log.
    expect(queue.items().map((item) => item.receipt?.number)).toEqual([2, 1]);
  });
});
