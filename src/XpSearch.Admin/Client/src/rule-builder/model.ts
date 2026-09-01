/*
 * The rule the builder edits, and the split between what the page stores (one set of conditions,
 * ADR-0022) and what it shows (the condition cards of design canvas 5a). The back end is
 * XpSearch.Admin.UIPages.RuleBuilder.
 */

export type QueryOperator = 'is' | 'contains' | 'startsWith';

/** The action types of the add menu, in the order design canvas 5b lists them. */
export const actionTypes = [
  'pin',
  'hide',
  'boost',
  'bury',
  'filterResults',
  'removeWord',
  'replaceWord',
  'replaceQuery',
  'redirect',
  'customData',
] as const;

export type ActionType = (typeof actionTypes)[number];

/** What each action type is called, and whether it is new in this release (canvas 5b). */
export const actionLabels: Record<ActionType, { readonly label: string; readonly hint?: string; readonly isNew?: boolean }> = {
  pin: { label: 'Pin an item' },
  hide: { label: 'Hide an item', isNew: true },
  boost: { label: 'Boost matching results' },
  bury: { label: 'Bury matching results' },
  filterResults: { label: 'Filter results', hint: 'attribute is value' },
  removeWord: { label: 'Remove word', isNew: true },
  replaceWord: { label: 'Replace word', isNew: true },
  replaceQuery: { label: 'Replace query', isNew: true },
  redirect: { label: 'Redirect' },
  customData: { label: 'Return custom data', isNew: true },
};

export interface Filter {
  attribute: string;
  value: string;
}

export interface Conditions {
  queryEnabled: boolean;
  queryOperator: QueryOperator;
  queryPattern: string;
  matchAnalyzed: boolean;
  filters: Filter[];
  contactGroup: string;
  language: string;
}

/** Which action types name one indexed item, and so get the item picker of design canvas 5h. */
export const itemActionTypes: readonly ActionType[] = ['pin', 'hide', 'boost', 'bury'];

/** Which action types are edited as `attribute is value` rows. Boost is both, by either field. */
export const attributeActionTypes: readonly ActionType[] = ['filterResults', 'boost', 'bury'];

/** One flat shape with every field any action needs; `type` says which of them are read. */
export interface Action {
  type: ActionType;
  targetId: string;
  /**
   * What `targetId` points at, filled in by the server when the rule is loaded. `null` means the
   * index no longer holds the id, which the summary row warns about rather than hiding.
   */
  targetTitle?: string | null;
  targetUrl?: string | null;
  position: number;
  filterExpression: string;
  multiplier: number;
  word: string;
  replacement: string;
  query: string;
  url: string;
  json: string;
}

export interface Rule {
  id: number;
  name: string;
  enabled: boolean;
  priority: number;
  validFrom: string;
  validTo: string;
  conditions: Conditions;
  actions: Action[];
}

export interface RuleError {
  readonly field: string;
  readonly message: string;
}

export interface SaveResult {
  readonly errors: RuleError[];
  readonly rule?: Rule;
  readonly error: string;
}

export interface ContactGroup {
  readonly codeName: string;
  readonly displayName: string;
}

/**
 * One condition card. A rule stores a single set of conditions, all of which must hold; the cards
 * are how the design splits that set up so each can be edited on its own (canvas 5a/5f). Every
 * card owns a disjoint part of the set, which `merge` puts back together and `conflicts` polices.
 */
export interface Fragment {
  readonly id: string;
  queryEnabled: boolean;
  queryOperator: QueryOperator;
  queryPattern: string;
  matchAnalyzed: boolean;
  filtersEnabled: boolean;
  filters: Filter[];
  contextEnabled: boolean;
  contactGroup: string;
  language: string;
}

let nextFragmentId = 0;

export const newFragment = (): Fragment => ({
  id: `condition-${++nextFragmentId}`,
  queryEnabled: false,
  queryOperator: 'contains',
  queryPattern: '',
  matchAnalyzed: false,
  filtersEnabled: false,
  filters: [],
  contextEnabled: false,
  contactGroup: '',
  language: '',
});

export const emptyAction = (type: ActionType): Action => ({
  type,
  targetId: '',
  targetTitle: null,
  targetUrl: null,
  position: 1,
  filterExpression: '',
  multiplier: 2,
  word: '',
  replacement: '',
  query: '',
  url: '',
  json: type === 'customData' ? '{\n  "banner": ""\n}' : '',
});

/**
 * Splits a stored rule into the cards the page shows: the query is one card, the filters and the
 * context another - which is exactly how canvas 5a reads a rule with both.
 */
export const split = (conditions: Conditions): Fragment[] => {
  const fragments: Fragment[] = [];

  if (conditions.queryEnabled) {
    fragments.push({
      ...newFragment(),
      queryEnabled: true,
      queryOperator: conditions.queryOperator,
      queryPattern: conditions.queryPattern,
      matchAnalyzed: conditions.matchAnalyzed,
    });
  }

  const hasFilters = conditions.filters.length > 0;
  const hasContext = conditions.contactGroup !== '' || conditions.language !== '';

  if (hasFilters || hasContext) {
    fragments.push({
      ...newFragment(),
      filtersEnabled: hasFilters,
      filters: conditions.filters.map((filter) => ({ ...filter })),
      contextEnabled: hasContext,
      contactGroup: conditions.contactGroup,
      language: conditions.language,
    });
  }

  return fragments;
};

/** Puts the cards back together into the one set of conditions the rule stores. */
export const merge = (fragments: Fragment[]): Conditions => {
  const query = fragments.find((fragment) => fragment.queryEnabled);
  const context = fragments.find((fragment) => fragment.contextEnabled);

  return {
    queryEnabled: query !== undefined,
    queryOperator: query?.queryOperator ?? 'contains',
    queryPattern: query?.queryPattern ?? '',
    matchAnalyzed: query?.matchAnalyzed ?? false,
    filters: fragments
      .filter((fragment) => fragment.filtersEnabled)
      .flatMap((fragment) => fragment.filters)
      .filter((filter) => filter.attribute !== '' || filter.value !== ''),
    contactGroup: context?.contactGroup ?? '',
    language: context?.language ?? '',
  };
};

/**
 * What the cards disagree about. A rule has one query condition and one context, so a second card
 * claiming either would silently lose - it is refused instead.
 */
export const conflicts = (fragments: Fragment[]): string[] => {
  const messages: string[] = [];

  if (fragments.filter((fragment) => fragment.queryEnabled).length > 1) {
    messages.push('Only one condition can have the Query toggle on — a rule matches one query pattern.');
  }

  if (fragments.filter((fragment) => fragment.contextEnabled).length > 1) {
    messages.push('Only one condition can have the Context toggle on — a rule has one contact group and one language.');
  }

  return messages;
};

/** Whether the cards say nothing at all, which is the one thing Save refuses outright (canvas 5d). */
export const isEmpty = (fragments: Fragment[]): boolean => {
  const conditions = merge(fragments);

  return (
    !conditions.queryEnabled && conditions.filters.length === 0 && conditions.contactGroup === '' && conditions.language === ''
  );
};

/**
 * Moves an action one place up or down. The order is behaviour - rewrites chain and custom data
 * merges in order - so this is the whole point of the up/down buttons of design canvas 5g.
 */
export const move = (actions: Action[], at: number, by: 1 | -1): Action[] => {
  const to = at + by;

  if (to < 0 || to >= actions.length) {
    return actions;
  }

  const moved = [...actions];

  [moved[at], moved[to]] = [moved[to], moved[at]];

  return moved;
};

/**
 * What the panel's Apply refuses to close on (design canvas 5d). It twins
 * XpSearch.Admin.Tuning.RuleValidation, which is the check that actually guards the save; this one
 * only saves the marketer a round trip, so the wording is deliberately the same.
 */
export const wrongWith = (action: Action): string[] => {
  const messages: string[] = [];
  const blank = (value: string) => value.trim() === '';

  switch (action.type) {
    case 'pin':
      if (blank(action.targetId)) {
        messages.push('Choose the item to pin.');
      }

      if (action.position < 1) {
        messages.push('Position counts from 1, the top of the first page.');
      }

      break;

    case 'hide':
      if (blank(action.targetId)) {
        messages.push('Choose the item to hide.');
      }

      break;

    case 'bury':
      if (blank(action.targetId)) {
        messages.push('Choose the item to bury.');
      }

      break;

    case 'boost':
      if (blank(action.targetId) && blank(action.filterExpression)) {
        messages.push('Choose an item to boost, or an attribute:value expression to boost by.');
      }

      if (action.multiplier <= 0) {
        messages.push('A multiplier of 0 or less would switch the rule off — use a number above 0.');
      }

      break;

    case 'filterResults':
      if (blank(action.filterExpression)) {
        messages.push('Enter the attribute:value pairs to keep.');
      }

      break;

    case 'removeWord':
      if (blank(action.word)) {
        messages.push('Enter the word to remove from the query.');
      }

      break;

    case 'replaceWord':
      if (blank(action.word) || blank(action.replacement)) {
        messages.push('Enter both the word to replace and what to put in its place.');
      }

      break;

    case 'replaceQuery':
      if (blank(action.query)) {
        messages.push('Enter the query to search for instead.');
      }

      break;

    case 'redirect':
      if (blank(action.url)) {
        messages.push('Enter where the visitor should be sent.');
      }

      break;

    case 'customData':
      if (!isJsonObject(action.json)) {
        messages.push('Not valid JSON — custom data has to be a JSON object, for example {"banner": "…"}.');
      }

      break;

    default:
      break;
  }

  return messages;
};

const isJsonObject = (json: string): boolean => {
  try {
    const parsed: unknown = JSON.parse(json);

    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed);
  } catch {
    return false;
  }
};
