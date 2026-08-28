import { useState } from 'react';
import {
  Button,
  ButtonColor,
  ButtonSize,
  Callout,
  CalloutPlacementType,
  CalloutType,
  Card,
  Checkbox,
  DateTimeRangeInput,
  Colors, Column,
  DropDownActionMenu,
  DropDownPlacement,
  FormItemWrapper,
  Headline,
  HeadlineSize,
  Input, LayoutAlignment,
  MenuItem, Row,
  SidePanelManager, Spacing,
  Tag,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { muted } from '../theme';
import { ConditionPanel } from './ConditionPanel';
import { ConsequenceCard } from './ConsequenceCard';
import {
  conflicts,
  consequenceLabels,
  consequenceTypes,
  emptyConsequence,
  isEmpty,
  merge,
  newFragment,
  split,
} from './model';
import type { Consequence, ConsequenceType, ContactGroup, Fragment, Rule, RuleError, SaveResult } from './model';
import { describe } from './summary';
import styles from './RuleBuilderTemplate.module.css';

/*
 * Client template of the if/then rule builder (ADR-0022), built to the owner's approved design
 * canvas: 5a the editor, 5b the add-consequence menu, 5c the five new editors, 5d validation,
 * 5e narrow at 1024, 5f the condition side panel. Registered as
 * "@xperience-community/xperience-search/RuleBuilder"; the back end is
 * XpSearch.Admin.UIPages.RuleBuilder.RuleBuilderPage.
 */

interface RuleBuilderProps {
  readonly indexName: string;
  readonly rule: Rule;
  readonly contactGroups: ContactGroup[];
  readonly languages: string[];
  readonly isNew: boolean;
  /** Whether to show the "converted from the previous format" note of canvas 5d. */
  readonly migrated: boolean;
  readonly error: string;
}

const Commands = {
  Save: 'Save',
  Cancel: 'Cancel',
  Delete: 'Delete',
};

/** The field name the server addresses a consequence card's errors to. */
const consequenceField = (index: number): string => `consequence:${index}`;

const messagesFor = (errors: RuleError[], field: string): string[] =>
  errors.filter((error) => error.field === field).map((error) => error.message);

export const RuleBuilderTemplate = ({ indexName, rule, contactGroups, languages, isNew, migrated, error }: RuleBuilderProps) => {
  const [name, setName] = useState(rule.name);
  const [enabled, setEnabled] = useState(rule.enabled);
  const [priority, setPriority] = useState(rule.priority);
  const [validFrom, setValidFrom] = useState(rule.validFrom);
  const [validTo, setValidTo] = useState(rule.validTo);
  const [fragments, setFragments] = useState<Fragment[]>(split(rule.conditions));
  const [consequences, setConsequences] = useState<Consequence[]>(rule.consequences);
  const [editing, setEditing] = useState<number | undefined>(undefined);
  const [errors, setErrors] = useState<RuleError[]>([]);
  const [saving, setSaving] = useState(false);
  const [noticeDismissed, setNoticeDismissed] = useState(false);

  const { execute: save } = usePageCommand<SaveResult, Rule>(Commands.Save, {
    after: (response) => {
      setSaving(false);
      setErrors(response?.errors ?? []);

      if (response?.error !== undefined && response.error !== '') {
        setErrors([{ field: 'page', message: response.error }]);
      }
    },
  });

  const { execute: cancel } = usePageCommand<void, void>(Commands.Cancel);
  const { execute: remove } = usePageCommand<void, void>(Commands.Delete);

  const local = conflicts(fragments);
  const blocked = isEmpty(fragments) || local.length > 0;

  const submit = () => {
    setSaving(true);
    setNoticeDismissed(true);
    void save({
      ...rule,
      name,
      enabled,
      priority,
      validFrom,
      validTo,
      conditions: merge(fragments),
      consequences,
    });
  };

  const applyFragment = (updated: Fragment) => {
    setFragments((current) => current.map((fragment) => (fragment.id === updated.id ? updated : fragment)));
    setEditing(undefined);
  };

  const addCondition = () => {
    setFragments((current) => [...current, newFragment()]);
    setEditing(fragments.length);
  };

  const addConsequence = (type: ConsequenceType) => setConsequences((current) => [...current, emptyConsequence(type)]);

  const changeConsequence = (at: number, values: Partial<Consequence>) =>
    setConsequences((current) => current.map((consequence, i) => (i === at ? { ...consequence, ...values } : consequence)));

  const pageErrors = messagesFor(errors, 'page');
  const nameErrors = messagesFor(errors, 'name');
  const conditionErrors = [...messagesFor(errors, 'conditions'), ...messagesFor(errors, 'query'), ...messagesFor(errors, 'filters')];

  if (error !== '') {
    return (
      <div className={styles.page}>
        <Callout
          type={CalloutType.FriendlyWarning}
          placement={CalloutPlacementType.OnDesk}
          subheadline="Friendly warning"
          headline="This rule cannot be edited here"
        >
          {error}
        </Callout>
      </div>
    );
  }

  return (
    <SidePanelManager>
      <div className={styles.page}>
        <div className={styles.header}>
          <div>
            <Headline size={HeadlineSize.L}>{isNew ? 'New rule' : name || 'Rule'}</Headline>
            <p style={muted}>
              Index <strong>{indexName}</strong>
            </p>
          </div>
          <div className={styles.headerActions}>
            {isNew ? null : (
              <Button
                label="Delete"
                destructive
                color={ButtonColor.Tertiary}
                onClick={() => {
                  void remove();
                }}
              />
            )}
            <Button
              label="Cancel"
              color={ButtonColor.Secondary}
              onClick={() => {
                void cancel();
              }}
            />
            <Button label="Save rule" color={ButtonColor.Primary} inProgress={saving} disabled={blocked} onClick={submit} />
          </div>
        </div>

        {migrated && !noticeDismissed ? (
          <Callout
            type={CalloutType.QuickTip}
            placement={CalloutPlacementType.OnDesk}
            subheadline="Quick tip"
            headline="Converted from the previous format"
            maxWidth="100%"
          >
            Converted from the previous format — one condition, one consequence. Nothing about its behaviour changed.
          </Callout>
        ) : null}

        {errors.length > 0 ? (
          <Callout
            type={CalloutType.FriendlyWarning}
            placement={CalloutPlacementType.OnDesk}
            subheadline="Friendly warning"
            headline="The rule cannot be saved"
            maxWidth="100%"
          >
            <p role="alert">
              {pageErrors.length > 0
                ? pageErrors.join(' ')
                : `Fix the ${errors.length === 1 ? 'error' : `${errors.length} errors`} below. Nothing was changed.`}
            </p>
          </Callout>
        ) : null}

        {local.map((message) => (
          <p key={message} className={styles.error} role="alert">
            {message}
          </p>
        ))}

        <Card>
          <Row alignY={LayoutAlignment.Center} spacing={Spacing.M}>
            <Column>
              <Input
                  label="Rule name"
                  markAsRequired
                  value={name}
                  invalid={nameErrors.length > 0}
                  validationMessage={nameErrors[0]}
                  explanationText="Shown in the ranking explanation, so name it after what it does."
                  onChange={(event) => setName(event.target.value)}
              />
            </Column>
            <Column>
              <Checkbox label="Enabled" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />
            </Column>
            <Column>
              <Input
                  label="Priority"
                  type="number"
                  value={String(priority)}
                  explanationText="Lower wins."
                  onChange={(event) => setPriority(Number(event.target.value) || 0)}
              />
            </Column>
            <Column>
              <FormItemWrapper
                  label="Runs"
                  explanationText={validFrom === '' && validTo === '' ? 'Empty = always.'
                      : validFrom === '' || validTo === '' ? `Open-ended window (${validFrom || '…'} – ${validTo || '…'}); picking a range replaces it.`
                      : undefined}>
                <DateTimeRangeInput
                    timeZone="UTC"
                    showTime={false}
                    allowClear
                    value={validFrom !== '' && validTo !== ''
                        ? {from: new Date(`${validFrom}T00:00:00Z`), to: new Date(`${validTo}T00:00:00Z`)}
                        : null}
                    onChange={(range) => {
                      if (range === null) {
                        setValidFrom('');
                        setValidTo('');
                        return;
                      }
                      setValidFrom(range.from.toISOString().slice(0, 10));
                      setValidTo(range.to.toISOString().slice(0, 10));
                    }}
                />
              </FormItemWrapper>
            </Column>
          </Row>
        </Card>

        <div className={styles.flow}>
          <div className={styles.flowLabel}>
            <div className={styles.sectionRow}>
              <Tag label="If" readOnly background={{ color: Colors.BackgroundTagXperienceViolet }} />
            </div>
            <p style={muted}>All conditions must hold. A rule needs at least one.</p>
          </div>
          <div className={styles.flowStack}>
            {fragments.length === 0 ? (
              <Callout
                type={CalloutType.QuickTip}
                placement={CalloutPlacementType.OnDesk}
                subheadline="Quick tip"
                headline="Start with a condition"
                maxWidth="100%"
              >
                A rule needs at least one condition — what the visitor searched, the filters on the request, or who they are
                (contact group, language). Consequences describe what happens when every condition holds.
              </Callout>
            ) : null}

            {fragments.map((fragment, index) => (
              <Card key={fragment.id}>
                <div className={styles.summaryRow}>
                  <div className={styles.summaryText}>
                    <span className={styles.summaryTitle}>{`Condition ${index + 1}`}</span>
                    <span>{describe(fragment, contactGroups)}</span>
                  </div>
                  <div className={styles.rowActions}>
                    <Button
                      label="Edit"
                      title={`Edit condition ${index + 1}`}
                      color={ButtonColor.Tertiary}
                      size={ButtonSize.XS}
                      onClick={() => setEditing(index)}
                    />
                    <Button
                      label="Remove"
                      title={`Remove condition ${index + 1}`}
                      icon="xp-bin"
                      destructive
                      color={ButtonColor.Quinary}
                      size={ButtonSize.XS}
                      onClick={() => setFragments((current) => current.filter((_, i) => i !== index))}
                    />
                  </div>
                </div>
              </Card>
            ))}

            {conditionErrors.map((message) => (
              <p key={message} className={styles.error} role="alert">
                {message}
              </p>
            ))}

            <div className={styles.addArea}>
              <Button label="Add condition" icon="xp-plus" color={ButtonColor.Tertiary} onClick={addCondition} />
            </div>
          </div>
        </div>

        <div className={styles.flow}>
          <div className={styles.flowLabel}>
            <div className={styles.sectionRow}>
              <Tag label="Then" readOnly background={{ color: Colors.BackgroundTagSkyBlue }} />
            </div>
            <p style={muted}>
              Applied in order. Pin, hide, boost, bury, filter, rewrite the query, redirect, or return custom data.
            </p>
          </div>
          <div className={styles.flowStack}>
            {consequences.map((consequence, index) => (
              <ConsequenceCard
                key={`${consequence.type}-${index}`}
                consequence={consequence}
                errors={messagesFor(errors, consequenceField(index))}
                onChange={(values) => changeConsequence(index, values)}
                onRemove={() => setConsequences((current) => current.filter((_, i) => i !== index))}
              />
            ))}

            <div className={styles.addArea}>
              <DropDownActionMenu
                placement={DropDownPlacement.BottomStart}
                renderTrigger={(ref, onTriggerClick) => (
                  <span ref={ref as React.RefObject<HTMLSpanElement>}>
                    <Button label="Add consequence" icon="xp-chevron-down" color={ButtonColor.Tertiary} onClick={onTriggerClick} />
                  </span>
                )}
              >
                {consequenceTypes.map((type) => (
                  <MenuItem
                    key={type}
                    primaryLabel={consequenceLabels[type].label}
                    secondaryLabel={consequenceLabels[type].hint}
                    trailingElement={
                      consequenceLabels[type].isNew
                        ? { type: 'label' as const, element: <Tag label="new" readOnly background={{ color: Colors.BackgroundTagNeonGreen }} /> }
                        : undefined
                    }
                    onClick={() => addConsequence(type)}
                  />
                ))}
              </DropDownActionMenu>
            </div>
          </div>
        </div>

        <ConditionPanel
          editing={editing === undefined ? undefined : fragments[editing]}
          index={editing ?? 0}
          contactGroups={contactGroups}
          languages={languages}
          onApply={applyFragment}
          onDiscard={() => setEditing(undefined)}
        />
      </div>
    </SidePanelManager>
  );
};
