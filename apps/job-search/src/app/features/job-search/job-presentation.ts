/** Date-only formatting never converts the server calendar date through UTC. */
export function closingDateLabel(value: string): string {
  return new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: 'long', day: 'numeric' }).format(new Date(value + 'T12:00:00'));
}
