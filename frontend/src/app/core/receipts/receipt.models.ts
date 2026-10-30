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

export interface LineItem {
  lineNumber: number;
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface ExtractionFlag {
  field: string;
  reason: string;
}

export interface ReceiptDetail {
  id: string;
  number: number;
  status: ReceiptStatus;
  vendorName: string | null;
  receiptDate: string | null;
  currency: string | null;
  subtotal: number | null;
  tax: number | null;
  total: number | null;
  paymentMethod: string | null;
  categoryId: string | null;
  categorySuggestedByAi: boolean;
  notes: string | null;
  failureReason: string | null;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  hasPreview: boolean;
  lineItems: LineItem[];
  extractionFlags: ExtractionFlag[];
  baseCurrency: string | null;
  exchangeRate: number | null;
  exchangeRateDate: string | null;
  amountCharged: number | null;
  baseCurrencyTotal: number | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface Category {
  id: string;
  name: string;
  color: string;
  monthlyBudget: number | null;
  isArchived: boolean;
}

export interface UpdateReceiptRequest {
  vendorName: string | null;
  receiptDate: string | null;
  currency: string | null;
  subtotal: number | null;
  tax: number | null;
  total: number | null;
  paymentMethod: string | null;
  categoryId: string | null;
  notes: string | null;
  lineItems: LineItemRequest[];
}

export interface LineItemRequest {
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface ExchangeRateQuote {
  from: string;
  to: string;
  rate: number;
  rateDate: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const RECEIPT_UPLOAD_LIMITS = {
  maxSizeBytes: 10 * 1024 * 1024,
  maxFilesPerUpload: 10,
  accept: 'image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf,.jpg,.jpeg,.png,.webp,.heic,.heif,.pdf',
  extensions: ['.jpg', '.jpeg', '.png', '.webp', '.heic', '.heif', '.pdf'],
} as const;
