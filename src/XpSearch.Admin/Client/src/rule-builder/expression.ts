import type { Filter } from './model';

/*
 * The stored filter expression - comma-separated `attribute:value` pairs, all of which must hold -
 * read and written as the attribute rows of design canvas 5h. It twins
 * XpSearch.Core.Tuning.RuleFilterExpression, which is what the query pipeline parses the stored
 * string with and what the server re-composes a submitted one through; the rows have to change as
 * the marketer types, with nothing saved, so the server's version cannot be asked. Keep the two in
 * step - see docs/internal/KNOWN-LIMITATIONS.md.
 */

/** Reads an expression into rows. Malformed pairs are dropped, exactly as the server drops them. */
export const parseExpression = (expression: string): Filter[] =>
  expression
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part !== '')
    .map((part) => {
      const separator = part.indexOf(':');

      return separator <= 0 || separator === part.length - 1
        ? undefined
        : { attribute: part.slice(0, separator).trim(), value: part.slice(separator + 1).trim() };
    })
    .filter((pair): pair is Filter => pair !== undefined);

/** Writes rows back into an expression, leaving out any row that is only half filled in. */
export const composeExpression = (rows: Filter[]): string =>
  rows
    .map((row) => ({ attribute: row.attribute.trim(), value: row.value.trim() }))
    .filter((row) => row.attribute !== '' && row.value !== '')
    .map((row) => `${row.attribute}:${row.value}`)
    .join(', ');
