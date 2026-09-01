import type { Action, ContactGroup, Fragment, QueryOperator } from './model';

/*
 * The one-line reading of a condition card and of an action row (design canvas 5a, 5g): every
 * toggle that is on, joined with a middle dot. It twins XpSearch.Admin.Tuning.RuleSummary, which
 * formats the same things for the rule listing - down to the wording, so the two screens read alike;
 * a row has to re-read the moment the side panel's Apply is clicked, with nothing saved, so the
 * server's version cannot be asked. See docs/internal/KNOWN-LIMITATIONS.md.
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

/** What an action's item reads as: its title, or the raw id when the index no longer holds it. */
export const itemLabel = (action: Action): string =>
  action.targetTitle === null || action.targetTitle === undefined || action.targetTitle === ''
    ? action.targetId
    : action.targetTitle;

/** Whether an action names an item the index no longer holds, which the row warns about. */
export const isOrphaned = (action: Action): boolean =>
  action.targetId.trim() !== '' && (action.targetTitle === null || action.targetTitle === undefined || action.targetTitle === '');

/** What an action with nothing filled in yet reads as. */
export const nothingChosen = 'Nothing chosen yet — open it to finish this action.';

/**
 * The one-line reading of one action (design canvas 5g), in the same words as
 * RuleSummary.Describe(RuleAction) - with the item's title in place of its id wherever the server
 * resolved one.
 */
export const describeAction = (action: Action): string => {
  const item = itemLabel(action);
  const multiplier = String(Number(action.multiplier.toFixed(2)));

  switch (action.type) {
    case 'pin':
      return item === '' ? nothingChosen : `Pin ${item} to position ${String(action.position)}`;

    case 'hide':
      return item === '' ? nothingChosen : `Hide ${item}`;

    case 'boost':
      return item === '' && action.filterExpression === ''
        ? nothingChosen
        : `Boost ${item === '' ? action.filterExpression : item} ×${multiplier}`;

    case 'bury':
      return item === '' && action.filterExpression === '' ? nothingChosen : `Bury ${item === '' ? action.filterExpression : item}`;

    case 'filterResults':
      return action.filterExpression === '' ? nothingChosen : `Filter results to ${action.filterExpression}`;

    case 'removeWord':
      return action.word === '' ? nothingChosen : `Remove the word “${action.word}”`;

    case 'replaceWord':
      return action.word === '' ? nothingChosen : `Replace “${action.word}” with “${action.replacement}”`;

    case 'replaceQuery':
      return action.query === '' ? nothingChosen : `Search instead for “${action.query}”`;

    case 'redirect':
      return action.url === '' ? nothingChosen : `Redirect to ${action.url}`;

    case 'customData':
      return 'Return custom data';

    default:
      return nothingChosen;
  }
};
