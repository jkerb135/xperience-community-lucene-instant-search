import { useEffect, useRef, useState } from 'react';
import {
  Button,
  ButtonColor,
  ButtonSize,
  Colors,
  Input,
  Tag,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { muted } from '../theme';
import type { Action } from './model';
import styles from './RuleBuilderTemplate.module.scss';

/*
 * The item picker of design canvas 5h: a debounced search over this index and a keyboard-navigable
 * result list. What is stored is the result id; the id itself is only shown behind "details", or as
 * the warning row of an action whose item has left the index.
 *
 * The search runs through the RuleBuilderPage SearchItems command, which reads the index without
 * journaling the query as a visitor's - see XpSearch.Admin.UIPages.RuleBuilder.RulePicker.
 */

/** One row of the result list, as the SearchItems command returns it. */
interface PickedItem {
  readonly id: string;
  readonly title: string | null;
  readonly url: string | null;
}

interface ItemSearchResult {
  readonly items: PickedItem[];
  readonly error: string;
}

interface ItemPickerProps {
  readonly action: Action;
  readonly label: string;
  readonly onPick: (item: PickedItem) => void;
}

/** How long typing settles before the index is asked, so a word is one search and not five. */
const debounceMs = 300;

export const ItemPicker = ({ action, label, onPick }: ItemPickerProps) => {
  const [query, setQuery] = useState('');
  const [items, setItems] = useState<PickedItem[]>([]);
  const [error, setError] = useState('');
  const [searching, setSearching] = useState(true);
  const [showId, setShowId] = useState(false);
  const options = useRef<(HTMLButtonElement | null)[]>([]);

  const { execute: search } = usePageCommand<ItemSearchResult, { query: string }>('SearchItems', {
    after: (response) => {
      setSearching(false);
      setItems(response?.items ?? []);
      setError(response?.error ?? '');
    },
  });

  useEffect(() => {
    setSearching(true);

    const handle = setTimeout(() => void search({ query }), debounceMs);

    return () => clearTimeout(handle);
    // The command object is rebuilt every render; re-running on it would search in a loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query]);

  const chosen = action.targetId.trim() !== '';
  const orphaned = chosen && (action.targetTitle === null || action.targetTitle === undefined || action.targetTitle === '');

  /*
   * Up and down move focus along the list, so Enter is the browser's own "press this button" and a
   * screen reader announces what is now focused. Nothing is faked with aria-activedescendant.
   */
  const keyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (items.length === 0 || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) {
      return;
    }

    event.preventDefault();

    const at = options.current.findIndex((option) => option !== null && option === document.activeElement);
    const next = at < 0 ? (event.key === 'ArrowDown' ? 0 : items.length - 1) : at + (event.key === 'ArrowDown' ? 1 : -1);

    options.current[next < 0 ? items.length - 1 : next >= items.length ? 0 : next]?.focus();
  };

  return (
    <div className={styles.picker} onKeyDown={keyDown}>
      <Input
        label={label}
        value={query}
        placeholder="Search this index — ↑↓ walks the results, Enter picks one"
        onChange={(event) => setQuery(event.target.value)}
      />

      {chosen ? (
        <p className={styles.pickerChosen}>
          {orphaned ? (
            <>
              <span>{action.targetId}</span>
              <Tag label="no longer in the index" readOnly background={{ color: Colors.BackgroundTagYellow }} />
            </>
          ) : (
            <>
              <span>
                Selected: <strong>{action.targetTitle}</strong>
              </span>
              {action.targetUrl === null || action.targetUrl === undefined || action.targetUrl === '' ? null : (
                <span style={muted}>{action.targetUrl}</span>
              )}
            </>
          )}
        </p>
      ) : null}

      {error === '' ? null : (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}

      <ul className={styles.pickerList} aria-label={label}>
        {items.map((item, index) => (
          <li key={item.id}>
            <button
              type="button"
              ref={(element) => {
                options.current[index] = element;
              }}
              aria-current={item.id === action.targetId}
              className={`${styles.pickerOption} ${item.id === action.targetId ? styles.pickerOptionSelected : ''}`}
              onClick={() => onPick(item)}
            >
              <span className={styles.pickerTitle}>{item.title === null || item.title === '' ? item.id : item.title}</span>
              <span className={styles.pickerUrl}>{item.url}</span>
            </button>
          </li>
        ))}
      </ul>

      <p style={muted} aria-live="polite">
        {searching ? 'Searching…' : items.length === 0 ? 'No matches. Try fewer words.' : `${String(items.length)} matches.`}
      </p>

      <div>
        <Button
          label={showId ? 'Hide details' : 'Details'}
          color={ButtonColor.Quinary}
          size={ButtonSize.XS}
          onClick={() => setShowId((current) => !current)}
        />
        {showId ? (
          <p style={muted}>Stored result id: {action.targetId === '' ? '—' : action.targetId}</p>
        ) : null}
      </div>
    </div>
  );
};
