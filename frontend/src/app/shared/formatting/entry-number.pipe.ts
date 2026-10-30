import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'entryNumber' })
export class EntryNumberPipe implements PipeTransform {
  transform(value: number): string {
    return value.toString().padStart(4, '0');
  }
}
