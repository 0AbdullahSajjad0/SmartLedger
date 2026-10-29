// Mirrors SmartLedger.Application.Overview.OverviewDto on the API.

import { CategorySpending } from '../categories/category.models';
import { ReceiptSummary } from '../receipts/receipt.models';

/** The monthly statement. Averages, changes, shares and over-budget lists are worked out from these. */
export interface Overview {
  /** "yyyy-MM". */
  month: string;
  baseCurrency: string;
  /** Approved receipts dated in the month, in the base currency. */
  spent: number;
  approvedCount: number;
  /** Sum of the active categories' budgets; zero when none are set. */
  totalBudget: number;
  previousMonthSpent: number;
  /** Waiting for review, from any month. */
  awaitingReview: number;
  /** AI couldn't read them. */
  unreadable: number;
  /** Every receipt in any status: zero means a brand-new account. */
  totalReceipts: number;
  /** Categories with spending in the month, biggest first. */
  categories: CategorySpending[];
  /** The newest ledger entries, in any status. */
  recent: ReceiptSummary[];
  /** AI's headline for the month, once its analysis has been written on the Reports page. */
  insightHeadline: string | null;
}
