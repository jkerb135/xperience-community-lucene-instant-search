import type { ContactGroup, Fragment, QueryOperator } from './model';

/*
 * The one-line reading of a condition card (design canvas 5a): every toggle that is on, joined with
 * a middle dot. It twins XpSearch.Admin.Tuning.RuleSummary, which formats the same thing for the
 * rule listing; a card has to re-read the moment the side panel's Apply is clicked, with nothing
 * saved, so the server's version cannot be asked. See docs/internal/KNOWN-LIMITATIONS.md.
 */

const separator = ' · ';

const operators: Record<QueryOperator, string> = {
  is: 'is',
  contains: 'contains',
  startsWith: 'starts with',
};

/** What a card that has nothing turned on yet reads as. */
export const nothingConfigured = 'Nothing configured yet — open it to choose what to match.';

export const describe = (fragment: Fragment, contactGroups: ContactGroup[]): string => {
  const parts: string[] = [];

  if (fragment.queryEnabled) {
    const pattern = fragment.queryPattern === '' ? 'any query' : `“${fragment.queryPattern}”`;

    parts.push(`Query ${operators[fragment.queryOperator]} ${pattern}`);

    if (fragment.matchAnalyzed) {
      parts.push('plurals & synonyms');
    }
  }

  if (fragment.filtersEnabled) {
    parts.push(
      ...fragment.filters
        .filter((filter) => filter.attribute !== '' || filter.value !== '')
        .map((filter) => `Filter ${filter.attribute} is ${filter.value}`),
    );
  }

  if (fragment.contextEnabled) {
    if (fragment.contactGroup !== '') {
      const group = contactGroups.find((candidate) => candidate.codeName === fragment.contactGroup);

      parts.push(`Contact group ${group?.displayName ?? fragment.contactGroup}`);
    }

    parts.push(fragment.language === '' ? 'any language' : `Language ${fragment.language}`);
  }

  return parts.length === 0 ? nothingConfigured : parts.join(separator);
};
