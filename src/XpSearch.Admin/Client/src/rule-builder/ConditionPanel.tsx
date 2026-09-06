import { useEffect, useState } from 'react';
import {
  Button,
  ButtonColor,
  Divider,
  DividerOrientation,
  Input,
  MenuItem,
  Select,
  SidePanel,
  SidePanelSize, Spacing, Stack,
  Switch,
  SwitchSize,
} from '@kentico/xperience-admin-components';

import { AttributeRows } from './AttributeRows';
import type { ContactGroup, Fragment, QueryOperator } from './model';
import styles from './RuleBuilderTemplate.module.scss';

/*
 * The condition side panel of the approved board docs/internal/design/rule-builder-panels/Main.dc.html
 * (ConditionExpression / ConditionEmpty are its other two states): Query / Filters / Context, each a
 * Switch with its fields indented under it, a Divider between them. Apply writes back to the summary
 * row only. Nothing here persists - the page's Save rule does that.
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
  /** The facetable attributes of the index, for the Filters rows (the AttributeRows board). */
  readonly attributes: string[];
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

export const ConditionPanel = ({ editing, index, attributes, contactGroups, languages, onApply, onDiscard }: ConditionPanelProps) => {
  const [draft, setDraft] = useState<Fragment | undefined>(editing);

  // The panel edits a copy: Discard and Esc have to leave the card exactly as it was.
  useEffect(() => setDraft(editing === undefined ? undefined : { ...editing, filters: editing.filters.map((f) => ({ ...f })) }), [editing]);

  const change = (values: Partial<Fragment>) => setDraft((current) => (current === undefined ? current : { ...current, ...values }));

  return (
    <SidePanel
      isVisible={draft !== undefined}
      size={SidePanelSize.Stackable}
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
        <Stack spacing={Spacing.XL}>
          {/* SidePanel has no subtitle slot, so the board's one-liner is the first body child. */}
          <p className={styles.panelSubtitle}>All parts you switch on must hold.</p>

          <div className={styles.toggleGroup}>
            <Switch
              size={SwitchSize.M}
              label="Query"
              value={draft.queryEnabled}
              onChange={(value) => change({ queryEnabled: value })}
            />
            {draft.queryEnabled ? (
              <div className={styles.toggleFields}>
                <div className={styles.fieldRow}>
                  <div className={styles.fieldOperator}>
                    <Select
                      label="The visitor's search"
                      value={draft.queryOperator}
                      onChange={(value) => change({ queryOperator: (value ?? 'contains') as QueryOperator })}
                    >
                      {operators.map((operator) => (
                        <MenuItem key={operator.id} primaryLabel={operator.label} value={operator.id} />
                      ))}
                    </Select>
                  </div>
                  <div className={styles.fieldGrow}>
                    <Input
                      label="Words to look for"
                      value={draft.queryPattern}
                      placeholder="e.g. grinder"
                      onChange={(event) => change({ queryPattern: event.target.value })}
                    />
                  </div>
                </div>
                <Switch
                  size={SwitchSize.M}
                  label="Match plurals & synonyms"
                  value={draft.matchAnalyzed}
                  onChange={(value) => change({ matchAnalyzed: value })}
                />
              </div>
            ) : null}
          </div>

          <Divider orientation={DividerOrientation.Horizontal} />

          <div className={styles.toggleGroup}>
            <Switch
              size={SwitchSize.M}
              label="Filters"
              value={draft.filtersEnabled}
              onChange={(value) => change({ filtersEnabled: value, filters: value && draft.filters.length === 0 ? [{ attribute: '', value: '' }] : draft.filters })}
            />
            {draft.filtersEnabled ? (
              <div className={styles.toggleFields}>
                <AttributeRows rows={draft.filters} attributes={attributes} onChange={(filters) => change({ filters })} />
              </div>
            ) : null}
          </div>

          <Divider orientation={DividerOrientation.Horizontal} />

          <div className={styles.toggleGroup}>
            <Switch
              size={SwitchSize.M}
              label="Context"
              value={draft.contextEnabled}
              onChange={(value) => change({ contextEnabled: value })}
            />
            {draft.contextEnabled ? (
              <div className={styles.toggleFields}>
                <div className={styles.fieldRow}>
                  <div className={styles.fieldGrow}>
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
                  </div>
                  <div className={styles.fieldLanguage}>
                    <Select label="Language" value={draft.language} onChange={(value) => change({ language: value ?? '' })}>
                      <MenuItem primaryLabel="Any" value="" />
                      {languages.map((code) => (
                        <MenuItem key={code} primaryLabel={code} value={code} />
                      ))}
                    </Select>
                  </div>
                </div>
              </div>
            ) : null}
          </div>
        </Stack>
      )}
    </SidePanel>
  );
};
