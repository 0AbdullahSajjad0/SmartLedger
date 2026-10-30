import { CategorySpending } from '../categories/category.models';
import { ReceiptSummary } from '../receipts/receipt.models';

export interface Overview {
  month: string;
  baseCurrency: string;
  spent: number;
  approvedCount: number;
  totalBudget: number;
  previousMonthSpent: number;
  awaitingReview: number;
  unreadable: number;
  totalReceipts: number;
  categories: CategorySpending[];
  recent: ReceiptSummary[];
  insightHeadline: string | null;
}
