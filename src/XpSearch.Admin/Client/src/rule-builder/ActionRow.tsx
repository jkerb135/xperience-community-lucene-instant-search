import { useRef } from 'react';
import type { DragEvent, KeyboardEvent } from 'react';
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
import { gripLabel } from './reorder';
import { describeAction, isOrphaned } from './summary';
import styles from './RuleBuilderTemplate.module.scss';

/*
 * One read-only action row of the Then column (design canvas 5g): its number, what it does in one
 * line, and a grip / Edit / Remove. Everything about the action is edited in ActionPanel.
 *
 * The grip is not decoration: actions apply in order, query rewrites chain and custom data merges in
 * order, so moving one changes what the rule does. It drags with a mouse and, because a drag is
 * nothing a keyboard can do, it also lifts on space or enter and then moves on the arrow keys - the
 * WAI-ARIA drag pattern. RuleBuilderTemplate owns the order and the live region that speaks it.
 */

/** The grip's DOM id, so the template can put focus back on it after the row re-renders. */
export const gripId = (index: number): string => `xps-action-grip-${String(index)}`;

interface ActionRowProps {
  readonly action: Action;
  readonly index: number;
  readonly count: number;
  /** Server-side messages for this action, shown under the row. */
  readonly errors: string[];
  /** Whether this row is the one currently lifted, by grip or by drag. */
  readonly lifted: boolean;
  /** Which edge of this row shows the insertion line, if either. */
  readonly insertion: 'before' | 'after' | undefined;
  readonly onEdit: () => void;
  readonly onRemove: () => void;
  /** Space or enter on the grip: lift this row, or drop the one already lifted. */
  readonly onToggleGrab: () => void;
  readonly onGrabMove: (by: 1 | -1) => void;
  readonly onGrabCancel: () => void;
  readonly onDragStart: () => void;
  /** The pointer is over this row; `after` is true below its middle. */
  readonly onDragOver: (after: boolean) => void;
  readonly onDrop: () => void;
  readonly onDragEnd: () => void;
}

/** Six dots, the conventional grip. Decorative: the button around it carries the name. */
const Grip = () => (
  <svg className={styles.gripIcon} viewBox="0 0 10 16" width="10" height="16" aria-hidden focusable="false">
    {[3, 8, 13].map((y) => (
      <g key={y}>
        <circle cx="2" cy={y} r="1.5" fill="currentColor" />
        <circle cx="8" cy={y} r="1.5" fill="currentColor" />
      </g>
    ))}
  </svg>
);

export const ActionRow = ({
  action,
  index,
  count,
  errors,
  lifted,
  insertion,
  onEdit,
  onRemove,
  onToggleGrab,
  onGrabMove,
  onGrabCancel,
  onDragStart,
  onDragOver,
  onDrop,
  onDragEnd,
}: ActionRowProps) => {
  const label = actionLabels[action.type].label;
  const row = useRef<HTMLDivElement>(null);

  const onGripKeyDown = (event: KeyboardEvent<HTMLSpanElement>) => {
    const handled = () => {
      event.preventDefault();
      event.stopPropagation();
    };

    switch (event.key) {
      case ' ':
      case 'Enter':
        handled();
        onToggleGrab();
        break;
      case 'ArrowUp':
      case 'ArrowDown':
        // Only while lifted, so an ungrabbed grip leaves the arrows to the page's own scrolling.
        if (lifted) {
          handled();
          onGrabMove(event.key === 'ArrowUp' ? -1 : 1);
        }
        break;
      case 'Escape':
        if (lifted) {
          handled();
          onGrabCancel();
        }
        break;
      default:
        break;
    }
  };

  const startDrag = (event: DragEvent<HTMLSpanElement>) => {
    event.dataTransfer.effectAllowed = 'move';
    // Firefox refuses to start a drag with an empty data transfer.
    event.dataTransfer.setData('text/plain', String(index));

    if (row.current !== null) {
      // Drag the whole row, not the few pixels of the grip the pointer actually took hold of.
      event.dataTransfer.setDragImage(row.current, 16, row.current.offsetHeight / 2);
    }

    onDragStart();
  };

  const overRow = (event: DragEvent<HTMLDivElement>) => {
    if (row.current === null) {
      return;
    }

    // preventDefault is what marks this a drop target; without it the browser refuses the drop.
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';

    const box = row.current.getBoundingClientRect();

    onDragOver(event.clientY > box.top + box.height / 2);
  };

  const classes = [
    styles.actionRow,
    lifted ? styles.actionRowLifted : '',
    insertion === 'before' ? styles.insertBefore : '',
    insertion === 'after' ? styles.insertAfter : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div
      ref={row}
      className={classes}
      onDragOver={overRow}
      onDrop={(event) => {
        event.preventDefault();
        onDrop();
      }}
    >
      <Card>
        <div className={styles.summaryRow}>
          <span
            id={gripId(index)}
            className={styles.grip}
            role="button"
            tabIndex={0}
            draggable
            aria-label={gripLabel(label, index, count)}
            aria-pressed={lifted}
            title={`Drag to reorder ${label}, or press space to move it with the arrow keys`}
            onKeyDown={onGripKeyDown}
            onDragStart={startDrag}
            onDragEnd={onDragEnd}
          >
            <Grip />
          </span>
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
    </div>
  );
};
