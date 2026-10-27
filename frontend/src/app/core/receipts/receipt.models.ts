// Mirrors SmartLedger.Application.Receipts contracts on the API.

export type ReceiptStatus = 'Processing' | 'NeedsReview' | 'Approved' | 'Rejected' | 'Failed';

export interface ReceiptSummary {
  id: string;
  number: number;
  status: ReceiptStatus;
  vendorName: string | null;
  receiptDate: string | null;
  currency: string | null;
  total: number | null;
  baseCurrencyTotal: number | null;
  categoryId: string | null;
  fileName: string;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/** Same limits the API enforces; checked in the browser first so problems show instantly. */
export const RECEIPT_UPLOAD_LIMITS = {
  maxSizeBytes: 10 * 1024 * 1024,
  maxFilesPerUpload: 10,
  accept: 'image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf,.jpg,.jpeg,.png,.webp,.heic,.heif,.pdf',
  extensions: ['.jpg', '.jpeg', '.png', '.webp', '.heic', '.heif', '.pdf'],
} as const;
