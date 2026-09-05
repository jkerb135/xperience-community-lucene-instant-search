import { useEffect, useState } from 'react';
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
  Colors,
  Divider,
  DividerOrientation,
  DropDownActionMenu,
  DropDownPlacement,
  FormItemWrapper,
  Headline,
  HeadlineSize,
  Input,
  MenuItem,
  SidePanelManager,
  Tag,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { ConditionPanel } from './ConditionPanel';
import { ActionPanel } from './ActionPanel';
import { ActionRow, gripId } from './ActionRow';
import {
  conflicts,
  actionLabels,
  actionTypes,
  emptyAction,
  isEmpty,
  merge,
  move,
  newFragment,
  split,
} from './model';
import type { Action, ActionType, ContactGroup, Fragment, Rule, RuleError, SaveResult } from './model';
import { announce, landing, lift, step } from './reorder';
import type { Grab } from './reorder';
import { describe } from './summary';
import styles from './RuleBuilderTemplate.module.scss';

/*
 * Client template of the if/then rule builder (ADR-0022), built to the owner's approved design
 * canvas: 5a the editor, 5b the add-action menu, 5c the five new editors, 5d validation,
 * 5e narrow at 1024, 5f the condition side panel, 5g the action side panel, 5h the pickers.
 * UX-3b restyled it to the board docs/internal/design/RuleBuilder.dc.html — a header card holding
 * the rule's settings, then the If and Then flows — with the layout in
 * RuleBuilderTemplate.module.scss and every region on its stock component.
 * Registered as
 * "@xperience-community/xperience-search/RuleBuilder"; the back end is
 * XpSearch.Admin.UIPages.RuleBuilder.RuleBuilderPage.
 */

interface RuleBuilderProps {
  readonly indexName: string;
  readonly rule: Rule;
  readonly contactGroups: ContactGroup[];
  readonly languages: string[];
  /** The facetable attributes of the index, which the attribute pickers offer (design canvas 5h). */
  readonly attributes: string[];
  readonly isNew: boolean;
  /** Whether to show the "converted from the previous format" note of canvas 5d. */
  readonly migrated: boolean;
  readonly error: string;
  /** "Variant B draft — <experiment>" when the rule belongs to an experiment's draft; empty when it is live. */
  readonly variantBanner: string;
  readonly variantBannerContent: string;
  /** Whether the rule can no longer be saved, because its experiment has started. */
  readonly readOnly: boolean;
}

const Commands = {
  Save: 'Save',
  Cancel: 'Cancel',
  Delete: 'Delete',
};

/** The field name the server addresses an action card's errors to. */
const actionField = (index: number): string => `action:${index}`;

const messagesFor = (errors: RuleError[], field: string): string[] =>
  errors.filter((error) => error.field === field).map((error) => error.message);

/** Whether an action is still exactly as the add menu created it, so discarding can drop it. */
const isBlank = (action: Action): boolean => {
  const blank = emptyAction(action.type);

  return (Object.keys(blank) as (keyof Action)[]).every((key) => action[key] === blank[key]);
};

export const RuleBuilderTemplate = ({
  indexName,
  rule,
  contactGroups,
  languages,
  attributes,
  isNew,
  migrated,
  error,
  variantBanner,
  variantBannerContent,
  readOnly,
}: RuleBuilderProps) => {
  const [name, setName] = useState(rule.name);
  const [enabled, setEnabled] = useState(rule.enabled);
  const [priority, setPriority] = useState(rule.priority);
  const [validFrom, setValidFrom] = useState(rule.validFrom);
  const [validTo, setValidTo] = useState(rule.validTo);
  const [fragments, setFragments] = useState<Fragment[]>(split(rule.conditions));
  const [actions, setActions] = useState<Action[]>(rule.actions);
  const [editing, setEditing] = useState<number | undefined>(undefined);
  const [editingAction, setEditingAction] = useState<number | undefined>(undefined);
  const [errors, setErrors] = useState<RuleError[]>([]);
  const [saving, setSaving] = useState(false);
  const [noticeDismissed, setNoticeDismissed] = useState(false);
  /** The row lifted by the keyboard grab, and the one held by a pointer drag; never both. */
  const [grab, setGrab] = useState<Grab | undefined>(undefined);
  const [dragFrom, setDragFrom] = useState<number | undefined>(undefined);
  /** Where a drop would land the dragged row: the gap before row `gap`, 0..actions.length. */
  const [gap, setGap] = useState<number | undefined>(undefined);
  const [reorderSaid, setReorderSaid] = useState('');
  /** The grip to put focus back on once the moved rows have re-rendered. */
  const [refocus, setRefocus] = useState<number | undefined>(undefined);

  // A move re-renders the rows in place, which would leave focus on whatever now sits where the
  // grabbed row was. Put it back on the row the marketer is still holding.
  useEffect(() => {
    if (refocus === undefined) {
      return;
    }

    document.getElementById(gripId(refocus))?.focus();
    setRefocus(undefined);
  }, [refocus]);

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
  const blocked = readOnly || isEmpty(fragments) || local.length > 0;

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
      actions,
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

  // Choosing a type from the menu opens the panel on a blank action of it (design canvas 5g).
  const addAction = (type: ActionType) => {
    setActions((current) => [...current, emptyAction(type)]);
    setEditingAction(actions.length);
  };

  const applyAction = (updated: Action) => {
    setActions((current) => current.map((action, i) => (i === editingAction ? updated : action)));
    setEditingAction(undefined);
  };

  // Discarding a brand new action leaves nothing behind: it was never filled in.
  const discardAction = () => {
    setActions((current) => current.filter((action, i) => i !== editingAction || !isBlank(action)));
    setEditingAction(undefined);
  };

  const labelAt = (index: number): string => actionLabels[actions[index].type].label;

  /*
   * The keyboard grab (WAI-ARIA drag pattern). The list really re-orders on every arrow key rather
   * than only on the drop, so the screen shows what the live region is saying.
   */
  const toggleGrab = (index: number) => {
    if (grab === undefined) {
      setGrab(lift(index));
      setReorderSaid(announce.grabbed(labelAt(index), index, actions.length));
      return;
    }

    setReorderSaid(announce.dropped(labelAt(grab.at), grab.at, actions.length));
    setGrab(undefined);
    setRefocus(grab.at);
  };

  const moveGrab = (by: 1 | -1) => {
    if (grab === undefined) {
      return;
    }

    const next = step(grab, by, actions.length);

    if (next.at === grab.at) {
      return;
    }

    setActions((current) => move(current, grab.at, next.at));
    setGrab(next);
    setReorderSaid(announce.moved(labelAt(grab.at), next.at, actions.length));
    setRefocus(next.at);
  };

  const cancelGrab = () => {
    if (grab === undefined) {
      return;
    }

    setActions((current) => move(current, grab.at, grab.from));
    setGrab(undefined);
    setReorderSaid(announce.cancelled(labelAt(grab.at), grab.from, actions.length));
    setRefocus(grab.from);
  };

  const endDrag = () => {
    setDragFrom(undefined);
    setGap(undefined);
  };

  const dropDragged = () => {
    if (dragFrom !== undefined && gap !== undefined) {
      const to = landing(dragFrom, gap);

      setActions((current) => move(current, dragFrom, to));
      setReorderSaid(announce.dropped(labelAt(dragFrom), to, actions.length));
    }

    endDrag();
  };

  /** Which edge of a row shows the insertion line while something is being dragged over it. */
  const insertionAt = (index: number): 'before' | 'after' | undefined => {
    if (dragFrom === undefined || gap === undefined || landing(dragFrom, gap) === dragFrom) {
      return undefined;
    }

    if (gap === index) {
      return 'before';
    }

    return gap === actions.length && index === actions.length - 1 ? 'after' : undefined;
  };

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
        <div className={styles.card}>
          <Card
            headline={
              <div className={styles.cardHeader}>
                <div className={styles.titleBlock}>
                  <div>{isNew ? 'New rule' : name || 'Rule'}</div>
                  <p className={styles.detail}>
                    Index <span className={styles.mono}>{indexName}</span>
                    {variantBanner === '' ? '' : ` · ${variantBanner}`}
                  </p>
                </div>
                <div className={styles.headerActions}>
                  {isNew || readOnly ? null : (
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
            }
          >
            <Divider orientation={DividerOrientation.Horizontal} />
            <div className={styles.settings}>
              <div className={styles.settingsName}>
                <Input
                  label="Rule name"
                  markAsRequired
                  value={name}
                  invalid={nameErrors.length > 0}
                  validationMessage={nameErrors[0]}
                  explanationText="Shown in the ranking explanation, so name it after what it does."
                  onChange={(event) => setName(event.target.value)}
                />
              </div>
              <div className={styles.settingsCheck}>
                <Checkbox label="Enabled" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />
              </div>
              <div className={styles.settingsSmall}>
                <Input
                  label="Priority"
                  type="number"
                  value={String(priority)}
                  explanationText="Lower wins."
                  onChange={(event) => setPriority(Number(event.target.value) || 0)}
                />
              </div>
              <div className={styles.settingsDate}>
                <FormItemWrapper
                  label="Runs"
                  explanationText={
                    validFrom === '' && validTo === ''
                      ? 'Empty = always.'
                      : validFrom === '' || validTo === ''
                        ? `Open-ended window (${validFrom || '…'} – ${validTo || '…'}); picking a range replaces it.`
                        : undefined
                  }
                >
                  <DateTimeRangeInput
                    timeZone="UTC"
                    showTime={false}
                    minDate={new Date()}
                    allowClear
                    value={
                      validFrom !== '' && validTo !== ''
                        ? { from: new Date(`${validFrom}T00:00:00Z`), to: new Date(`${validTo}T00:00:00Z`) }
                        : null
                    }
                    onChange={(range: any) => {
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
              </div>
            </div>
          </Card>
        </div>

        {variantBanner === '' ? null : (
          <Callout
            type={readOnly ? CalloutType.FriendlyWarning : CalloutType.QuickTip}
            placement={CalloutPlacementType.OnDesk}
            subheadline={readOnly ? 'Friendly warning' : 'Quick tip'}
            headline={variantBanner}
            maxWidth="100%"
          >
            {variantBannerContent}
          </Callout>
        )}

        {migrated && !noticeDismissed ? (
          <Callout
            type={CalloutType.QuickTip}
            placement={CalloutPlacementType.OnDesk}
            subheadline="Quick tip"
            headline="Converted from the previous format"
            maxWidth="100%"
          >
            Converted from the previous format — one condition, one action. Nothing about its behaviour changed.
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

        <div className={styles.flow}>
          <div className={styles.flowLabel}>
            <div className={styles.sectionRow}>
              <Headline size={HeadlineSize.L}>Condition</Headline>
              <Tag label="If" readOnly background={{ color: Colors.BackgroundTagXperienceViolet }} />
            </div>
            <p className={styles.detail}>All conditions must hold. A rule needs at least one.</p>
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
                (contact group, language). Actions describe what happens when every condition holds.
              </Callout>
            ) : null}

            {fragments.map((fragment, index) => (
              <div className={styles.summaryCard} key={fragment.id}>
                <Card>
                  <div className={styles.summaryRow}>
                    <div className={styles.summaryText}>
                      <span className={styles.summaryTitle}>{`Condition ${index + 1}`}</span>
                      <span className={styles.detail}>{describe(fragment, contactGroups)}</span>
                    </div>
                    <div className={styles.rowActions}>
                      <Button
                        label="Edit"
                        title={`Edit condition ${index + 1}`}
                        color={ButtonColor.Tertiary}
                        size={ButtonSize.S}
                        onClick={() => setEditing(index)}
                      />
                      <Button
                        label="Delete"
                        title={`Delete condition ${index + 1}`}
                        color={ButtonColor.Tertiary}
                        size={ButtonSize.S}
                        onClick={() => setFragments((current) => current.filter((_, i) => i !== index))}
                      />
                    </div>
                  </div>
                </Card>
              </div>
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
              <Headline size={HeadlineSize.L}>Action</Headline>
              <Tag label="Then" readOnly background={{ color: Colors.BackgroundTagXperienceViolet }} />
            </div>
            <p className={styles.detail}>
              Applied in order. Pin, hide, boost, bury, filter, rewrite the query, redirect, or return custom data.
            </p>
          </div>
          <div className={styles.flowStack}>
            {actions.map((action, index) => (
              <ActionRow
                key={index}
                action={action}
                index={index}
                count={actions.length}
                errors={messagesFor(errors, actionField(index))}
                lifted={grab?.at === index || dragFrom === index}
                insertion={insertionAt(index)}
                onEdit={() => setEditingAction(index)}
                onRemove={() => setActions((current) => current.filter((_, i) => i !== index))}
                onToggleGrab={() => toggleGrab(index)}
                onGrabMove={moveGrab}
                onGrabCancel={cancelGrab}
                onDragStart={() => setDragFrom(index)}
                onDragOver={(after) => setGap(after ? index + 1 : index)}
                onDrop={dropDragged}
                onDragEnd={endDrag}
              />
            ))}

            <div className={styles.visuallyHidden} role="status" aria-live="polite">
              {reorderSaid}
            </div>

            <div className={styles.addArea}>
              <DropDownActionMenu
                placement={DropDownPlacement.BottomStart}
                renderTrigger={(ref, onTriggerClick) => (
                  <span ref={ref as React.RefObject<HTMLSpanElement>}>
                    <Button label="Add action" icon="xp-chevron-down" color={ButtonColor.Tertiary} onClick={onTriggerClick} />
                  </span>
                )}
              >
                {actionTypes.map((type) => (
                  <MenuItem
                    key={type}
                    primaryLabel={actionLabels[type].label}
                    secondaryLabel={actionLabels[type].hint}
                    trailingElement={
                      actionLabels[type].isNew
                        ? { type: 'label' as const, element: <Tag label="new" readOnly background={{ color: Colors.BackgroundTagNeonGreen }} /> }
                        : undefined
                    }
                    onClick={() => addAction(type)}
                  />
                ))}
              </DropDownActionMenu>
            </div>
          </div>
        </div>

        <ConditionPanel
          editing={editing === undefined ? undefined : fragments[editing]}
          index={editing ?? 0}
          attributes={attributes}
          contactGroups={contactGroups}
          languages={languages}
          onApply={applyFragment}
          onDiscard={() => setEditing(undefined)}
        />

        <ActionPanel
          editing={editingAction === undefined ? undefined : actions[editingAction]}
          index={editingAction ?? 0}
          attributes={attributes}
          errors={editingAction === undefined ? [] : messagesFor(errors, actionField(editingAction))}
          onApply={applyAction}
          onDiscard={discardAction}
        />
      </div>
    </SidePanelManager>
  );
};
