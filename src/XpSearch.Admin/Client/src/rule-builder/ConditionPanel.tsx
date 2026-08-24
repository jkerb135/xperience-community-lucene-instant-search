import { useEffect, useState } from 'react';
import {
  Button,
  ButtonColor,
  ButtonSize,
  Input,
  MenuItem,
  Select,
  SidePanel,
  SidePanelSize,
  Switch,
  SwitchSize,
} from '@kentico/xperience-admin-components';

import type { ContactGroup, Fragment, QueryOperator } from './model';
import styles from './RuleBuilderTemplate.module.css';

/*
 * The condition side panel of design canvas 5f: Query / Filters / Context toggles, Apply writing
 * back to the summary row only. Nothing here persists — the page's Save rule does that.
 *
 * The package exports a SidePanel (verified in
 * node_modules/@kentico/xperience-admin-components/dist/entry.d.ts: `export declare const SidePanel`
 * with headline/footer/isVisible/onClose/size), so the canvas's fallback of building a drawer out of
 * Card primitives is not needed. It brings its own focus trap, its own Esc handling — both routed
 * through onClose — and the full-width behaviour at narrow widths.
 */

interface ConditionPanelProps {
  /** The card being edited, or undefined when the panel is closed. */
  readonly editing?: Fragment;
  readonly index: number;
  readonly contactGroups: ContactGroup[];
  readonly languages: string[];
  readonly onApply: (fragment: Fragment) => void;
  readonly onDiscard: () => void;
}

const operators: { readonly id: QueryOperator; readonly label: string }[] = [
  { id: 'contains', label: 'Contains' },
  { id: 'is', label: 'Is exactly' },
  { id: 'startsWith', label: 'Starts with' },
];

export const ConditionPanel = ({ editing, index, contactGroups, languages, onApply, onDiscard }: ConditionPanelProps) => {
  const [draft, setDraft] = useState<Fragment | undefined>(editing);

  // The panel edits a copy: Discard and Esc have to leave the card exactly as it was.
  useEffect(() => setDraft(editing === undefined ? undefined : { ...editing, filters: editing.filters.map((f) => ({ ...f })) }), [editing]);

  const change = (values: Partial<Fragment>) => setDraft((current) => (current === undefined ? current : { ...current, ...values }));

  const changeFilter = (at: number, values: Partial<Fragment['filters'][number]>) =>
    setDraft((current) =>
      current === undefined ? current : { ...current, filters: current.filters.map((filter, i) => (i === at ? { ...filter, ...values } : filter)) },
    );

  return (
    <SidePanel
      isVisible={draft !== undefined}
      size={SidePanelSize.Wrapped}
      headline={`Condition ${index + 1}`}
      tooltips={{ close: 'Discard' }}
      // Esc, the close button and a click outside all arrive here, and all of them discard.
      onClose={onDiscard}
      footer={
        <div className={styles.panelFooter}>
          <Button label="Discard" color={ButtonColor.Secondary} onClick={onDiscard} />
          <Button label="Apply" color={ButtonColor.Primary} onClick={() => draft !== undefined && onApply(draft)} />
        </div>
      }
    >
      {draft === undefined ? null : (
        <>
          <div className={styles.toggleGroup}>
            <Switch
              size={SwitchSize.M}
              label="Query"
              value={draft.queryEnabled}
              onChange={(value) => change({ queryEnabled: value })}
            />
            {draft.queryEnabled ? (
              <div className={styles.toggleFields}>
                <Select
                  label="The visitor's search"
                  value={draft.queryOperator}
                  onChange={(value) => change({ queryOperator: (value ?? 'contains') as QueryOperator })}
                >
                  {operators.map((operator) => (
                    <MenuItem key={operator.id} primaryLabel={operator.label} value={operator.id} />
                  ))}
                </Select>
                <Input
                  label="Words to look for"
                  value={draft.queryPattern}
                  placeholder="e.g. grinder"
                  onChange={(event) => change({ queryPattern: event.target.value })}
                />
                <Switch
                  size={SwitchSize.M}
                  label="Match plurals & synonyms"
                  value={draft.matchAnalyzed}
                  onChange={(value) => change({ matchAnalyzed: value })}
                />
              </div>
            ) : null}
          </div>

          <div className={styles.toggleGroup}>
            <Switch
              size={SwitchSize.M}
              label="Filters"
              value={draft.filtersEnabled}
              onChange={(value) => change({ filtersEnabled: value, filters: value && draft.filters.length === 0 ? [{ attribute: '', value: '' }] : draft.filters })}
            />
            {draft.filtersEnabled ? (
              <div className={styles.toggleFields}>
                {draft.filters.map((filter, at) => (
                  <div key={at} className={styles.filterRow}>
                    <div className={styles.fieldGrow}>
                      <Input
                        label={at === 0 ? 'Attribute' : undefined}
                        value={filter.attribute}
                        placeholder="e.g. contentType"
                        onChange={(event) => changeFilter(at, { attribute: event.target.value })}
                      />
                    </div>
                    <span className={styles.filterIs}>is</span>
                    <div className={styles.fieldGrow}>
                      <Input
                        label={at === 0 ? 'Value' : undefined}
                        value={filter.value}
                        onChange={(event) => changeFilter(at, { value: event.target.value })}
                      />
                    </div>
                    <Button
                      label="Remove"
                      title={`Remove filter ${at + 1}`}
                      icon="xp-times"
                      color={ButtonColor.Quinary}
                      size={ButtonSize.XS}
                      onClick={() => change({ filters: draft.filters.filter((_, i) => i !== at) })}
                    />
                  </div>
                ))}
                <div>
                  <Button
                    label="Add"
                    color={ButtonColor.Tertiary}
                    size={ButtonSize.XS}
                    onClick={() => change({ filters: [...draft.filters, { attribute: '', value: '' }] })}
                  />
                </div>
              </div>
            ) : null}
          </div>

          <div className={styles.toggleGroup}>
            <Switch
              size={SwitchSize.M}
              label="Context"
              value={draft.contextEnabled}
              onChange={(value) => change({ contextEnabled: value })}
            />
            {draft.contextEnabled ? (
              <div className={styles.toggleFields}>
                <Select
                  label="Contact group"
                  value={draft.contactGroup}
                  onChange={(value) => change({ contactGroup: value ?? '' })}
                >
                  <MenuItem primaryLabel="Everyone" value="" />
                  {contactGroups.map((group) => (
                    <MenuItem key={group.codeName} primaryLabel={group.displayName} value={group.codeName} />
                  ))}
                </Select>
                <Select label="Language" value={draft.language} onChange={(value) => change({ language: value ?? '' })}>
                  <MenuItem primaryLabel="Any" value="" />
                  {languages.map((code) => (
                    <MenuItem key={code} primaryLabel={code} value={code} />
                  ))}
                </Select>
              </div>
            ) : null}
          </div>
        </>
      )}
    </SidePanel>
  );
};
