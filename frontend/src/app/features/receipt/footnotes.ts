import { ExtractionFlag } from '../../core/receipts/receipt.models';

export interface Footnote extends ExtractionFlag {
  number: number;
}

export function numberFootnotes(flags: readonly ExtractionFlag[]): Footnote[] {
  return flags.map((flag, index) => ({ ...flag, number: index + 1 }));
}
