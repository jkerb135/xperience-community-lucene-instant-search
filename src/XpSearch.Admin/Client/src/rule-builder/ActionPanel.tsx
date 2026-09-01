import { useEffect, useState } from 'react';
import {
  Button,
  ButtonColor,
  Input,
  SidePanel,
  SidePanelSize,
  Spacing,
  Stack,
  TextArea,
} from '@kentico/xperience-admin-components';

import { muted } from '../theme';
import { AttributeRows } from './AttributeRows';
import { ItemPicker } from './ItemPicker';
import { composeExpression, parseExpression } from './expression';
import { actionLabels, wrongWith } from './model';
import type { Action } from './model';
import styles from './RuleBuilderTemplate.module.scss';

/*
 * One action's side panel (design canvas 5g): the same SidePanel machinery the condition panel uses
 * - focus trap, Esc and the close button both routed through onClose to Discard, full width at
 * narrow widths - with one body per action type. Apply is local; the page's Save rule persists.
 */

interface ActionPanelProps {
  /** The action being edited, or undefined when the panel is closed. */
  readonly editing?: Action;
  readonly index: number;
  /** The facetable attributes of the index, for the attribute rows. */
  readonly attributes: string[];
  /** Server-side messages for this action, shown at the top of the panel. */
  readonly errors: string[];
  readonly onApply: (action: Action) => void;
  readonly onDiscard: () => void;
}

/** What each body says under its fields, so the difference between hide and bury is on screen. */
const notes: Partial<Record<Action['type'], string>> = {
  hide: 'The item is removed from the results entirely while the rule matches — unlike Bury, which only sinks it.',
  removeWord: 'The word is dropped before the search runs, like a stopword: “cheap grinder” searches as “grinder”.',
  replaceQuery: 'The whole query is substituted before the search runs.',
  filterResults: 'Only results matching every row below are shown while this rule fires.',
  customData:
    'Attached to the response verbatim; the JS client exposes it to widgets and behaviours. Invalid JSON blocks Save.',
};

export const ActionPanel = ({ editing, index, attributes, errors, onApply, onDiscard }: ActionPanelProps) => {
  const [draft, setDraft] = useState<Action | undefined>(editing);
  const [refused, setRefused] = useState<string[]>([]);

  // The panel edits a copy: Discard and Esc have to leave the row exactly as it was.
  useEffect(() => {
    setDraft(editing === undefined ? undefined : { ...editing });
    setRefused([]);
  }, [editing]);

  const change = (values: Partial<Action>) => setDraft((current) => (current === undefined ? current : { ...current, ...values }));

  const apply = () => {
    if (draft === undefined) {
      return;
    }

    const wrong = wrongWith(draft);

    setRefused(wrong);

    if (wrong.length === 0) {
      onApply(draft);
    }
  };

  const body = (action: Action) => {
    const item = (label: string) => (
      <ItemPicker
        action={action}
        label={label}
        onPick={(picked) => change({ targetId: picked.id, targetTitle: picked.title, targetUrl: picked.url })}
      />
    );

    const rows = (
      <AttributeRows
        rows={parseExpression(action.filterExpression)}
        attributes={attributes}
        onChange={(next) => change({ filterExpression: composeExpression(next) })}
      />
    );

    switch (action.type) {
      case 'pin':
        return (
          <>
            {item('Find the item')}
            <div className={styles.fieldSmall}>
              <Input
                label="Position"
                type="number"
                value={String(action.position)}
                onChange={(event) => change({ position: Number(event.target.value) || 0 })}
              />
            </div>
          </>
        );

      case 'hide':
        return item('Find the item');

      case 'bury':
        return (
          <>
            {item('Find the item')}
            <p style={muted}>…or bury everything matching:</p>
            {rows}
          </>
        );

      case 'boost':
        return (
          <>
            {item('Find the item')}
            <p style={muted}>…or boost everything matching:</p>
            {rows}
            <div className={styles.fieldSmall}>
              <Input
                label="Multiplier"
                type="number"
                value={String(action.multiplier)}
                onChange={(event) => change({ multiplier: Number(event.target.value) || 0 })}
              />
            </div>
          </>
        );

      case 'filterResults':
        return rows;

      case 'removeWord':
        return (
          <Input
            label="Word"
            value={action.word}
            placeholder="e.g. cheap"
            onChange={(event) => change({ word: event.target.value })}
          />
        );

      case 'replaceWord':
        return (
          <>
            <Input
              label="Replace"
              value={action.word}
              placeholder="e.g. mill"
              onChange={(event) => change({ word: event.target.value })}
            />
            <Input
              label="…with"
              value={action.replacement}
              placeholder="e.g. grinder"
              onChange={(event) => change({ replacement: event.target.value })}
            />
          </>
        );

      case 'replaceQuery':
        return (
          <Input
            label="Search instead for"
            value={action.query}
            placeholder="e.g. hand grinder"
            onChange={(event) => change({ query: event.target.value })}
          />
        );

      case 'redirect':
        return (
          <Input
            label="Send the visitor to"
            value={action.url}
            placeholder="/campaigns/grinder-week"
            onChange={(event) => change({ url: event.target.value })}
          />
        );

      case 'customData':
        return (
          <div className={styles.jsonArea}>
            <TextArea label="JSON" value={action.json} minRows={4} onChange={(event) => change({ json: event.target.value })} />
          </div>
        );

      default:
        return null;
    }
  };

  return (
    <SidePanel
      isVisible={draft !== undefined}
      size={SidePanelSize.Full}
      headline={draft === undefined ? '' : `${String(index + 1)} · ${actionLabels[draft.type].label}`}
      tooltips={{ close: 'Discard' }}
      // Esc, the close button and a click outside all arrive here, and all of them discard.
      onClose={onDiscard}
      footer={
        <div className={styles.panelFooter}>
          <Button label="Discard" color={ButtonColor.Secondary} onClick={onDiscard} />
          <Button label="Apply" color={ButtonColor.Primary} onClick={apply} />
        </div>
      }
    >
      {draft === undefined ? null : (
        <Stack spacing={Spacing.L}>
          {[...errors, ...refused].map((message) => (
            <p key={message} className={styles.error} role="alert">
              {message}
            </p>
          ))}
          {notes[draft.type] === undefined ? null : <p style={muted}>{notes[draft.type]}</p>}
          {body(draft)}
        </Stack>
      )}
    </SidePanel>
  );
};
