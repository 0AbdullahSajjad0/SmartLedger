/**
 * Hands a downloaded file to the browser's "save" through a temporary link. Used for files fetched through the API
 * (with the access token), which a plain link couldn't download.
 */
export function saveFile(blob: Blob, name: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  // The click starts the download straight away; the address is only needed until then.
  setTimeout(() => URL.revokeObjectURL(url));
}
