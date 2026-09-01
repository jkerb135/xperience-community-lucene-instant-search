import {
  Button,
  ButtonColor,
  ButtonSize,
  Card,
  Colors,
  Tag,
} from '@kentico/xperience-admin-components';

import { actionLabels } from './model';
import type { Action } from './model';
import { describeAction, isOrphaned } from './summary';
import styles from './RuleBuilderTemplate.module.scss';

/*
 * One read-only action row of the Then column (design canvas 5g): its number, what it does in one
 * line, and Edit / move / remove. Everything about the action is edited in ActionPanel.
 *
 * The move buttons are not decoration: actions apply in order, query rewrites chain and custom data
 * merges in order, so moving one changes what the rule does. Each button says which action it moves
 * and where, because a screen reader gets no other clue that the list re-ordered.
 */

interface ActionRowProps {
  readonly action: Action;
  readonly index: number;
  readonly count: number;
  /** Server-side messages for this action, shown under the row. */
  readonly errors: string[];
  readonly onEdit: () => void;
  readonly onMove: (by: 1 | -1) => void;
  readonly onRemove: () => void;
}

export const ActionRow = ({ action, index, count, errors, onEdit, onMove, onRemove }: ActionRowProps) => {
  const label = actionLabels[action.type].label;
  const position = `${String(index + 1)} of ${String(count)}`;

  return (
    <Card>
      <div className={styles.summaryRow}>
        <div className={styles.summaryText}>
          <span className={styles.summaryTitle}>{`${String(index + 1)} · ${label}`}</span>
          <span>
            {describeAction(action)}
            {isOrphaned(action) ? (
              <>
                {' '}
                <Tag label="no longer in the index" readOnly background={{ color: Colors.BackgroundTagYellow }} />
              </>
            ) : null}
          </span>
        </div>
        <div className={styles.rowActions}>
          <Button
            label="Move up"
            title={`Move ${label} up — it is ${position}`}
            icon="xp-arrow-up"
            color={ButtonColor.Quinary}
            size={ButtonSize.XS}
            disabled={index === 0}
            onClick={() => onMove(-1)}
          />
          <Button
            label="Move down"
            title={`Move ${label} down — it is ${position}`}
            icon="xp-arrow-down"
            color={ButtonColor.Quinary}
            size={ButtonSize.XS}
            disabled={index === count - 1}
            onClick={() => onMove(1)}
          />
          <Button
            label="Edit"
            title={`Edit ${label}`}
            color={ButtonColor.Tertiary}
            size={ButtonSize.XS}
            onClick={onEdit}
          />
          <Button
            label="Remove"
            title={`Remove ${label}`}
            icon="xp-bin"
            destructive
            color={ButtonColor.Quinary}
            size={ButtonSize.XS}
            onClick={onRemove}
          />
        </div>
      </div>
      {errors.map((message) => (
        <p key={message} className={styles.error} role="alert">
          {message}
        </p>
      ))}
    </Card>
  );
};
