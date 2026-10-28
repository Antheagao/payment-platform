/** Joins class names, skipping empty and false values, so a caller's `className` merges with the defaults. */
export function classNames(...names: (string | false | null | undefined)[]): string {
  return names.filter(Boolean).join(" ");
}
