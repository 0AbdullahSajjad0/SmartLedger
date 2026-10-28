import { ExtractionFlag } from '../../core/receipts/receipt.models';

export interface Footnote extends ExtractionFlag {
  number: number;
}

/** AI's "please check" notes, numbered like footnotes: "Paid with ¹" → "¹ One digit was faded." */
export function numberFootnotes(flags: readonly ExtractionFlag[]): Footnote[] {
  return flags.map((flag, index) => ({ ...flag, number: index + 1 }));
}
