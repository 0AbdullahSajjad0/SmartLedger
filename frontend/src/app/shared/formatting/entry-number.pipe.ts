import { Pipe, PipeTransform } from '@angular/core';

/** Ledger entry numbers are shown with four digits: 7 → "0007". */
@Pipe({ name: 'entryNumber' })
export class EntryNumberPipe implements PipeTransform {
  transform(value: number): string {
    return value.toString().padStart(4, '0');
  }
}
