import {
  Button,
  ButtonColor,
  ButtonSize,
  Card,
  Input,
  TextArea,
} from '@kentico/xperience-admin-components';

import { muted } from '../theme';
import { consequenceLabels } from './model';
import type { Consequence } from './model';
import styles from './RuleBuilderTemplate.module.css';

/*
 * One card of the Then column (design canvas 5a for pin and boost, 5c for the five new editors).
 * The five new ones are hide, remove word, replace word, replace query and return custom data;
 * custom data is the monospace TextArea, and invalid JSON blocks Save (5d).
 */

interface ConsequenceCardProps {
  readonly consequence: Consequence;
  /** Server-side messages for this card, shown under it and echoed on its fields. */
  readonly errors: string[];
  readonly onChange: (values: Partial<Consequence>) => void;
  readonly onRemove: () => void;
}

/** What each editor says under its fields, so the difference between hide and bury is on screen. */
const notes: Partial<Record<Consequence['type'], string>> = {
  hide: 'The item is removed from the results entirely while the rule matches — unlike Bury, which only sinks it.',
  removeWord: 'The word is dropped before the search runs, like a stopword: “cheap grinder” searches as “grinder”.',
  replaceQuery: 'The whole query is substituted before the search runs.',
  customData:
    'Attached to the response verbatim; the JS client exposes it to widgets and behaviours. Invalid JSON blocks Save.',
};

export const ConsequenceCard = ({ consequence, errors, onChange, onRemove }: ConsequenceCardProps) => {
  const invalid = errors.length > 0;
  const note = notes[consequence.type];

  const targetInput = (label: string) => (
    <div className={styles.fieldGrow}>
      <Input
        label={label}
        value={consequence.targetId}
        invalid={invalid}
        placeholder="Result id from the search response"
        onChange={(event) => onChange({ targetId: event.target.value })}
      />
    </div>
  );

  const fields = () => {
    switch (consequence.type) {
      case 'pin':
        return (
          <>
            {targetInput('Item')}
            <div className={styles.fieldSmall}>
              <Input
                label="Position"
                type="number"
                value={String(consequence.position)}
                invalid={invalid}
                onChange={(event) => onChange({ position: Number(event.target.value) || 0 })}
              />
            </div>
          </>
        );

      case 'hide':
        return targetInput('Item');

      case 'bury':
        return targetInput('Item');

      case 'boost':
        return (
          <>
            {targetInput('Item')}
            <div className={styles.fieldGrow}>
              <Input
                label="…or an attribute:value expression"
                value={consequence.filterExpression}
                placeholder="e.g. Category:coffee"
                onChange={(event) => onChange({ filterExpression: event.target.value })}
              />
            </div>
            <div className={styles.fieldSmall}>
              <Input
                label="Multiplier"
                type="number"
                value={String(consequence.multiplier)}
                invalid={invalid}
                onChange={(event) => onChange({ multiplier: Number(event.target.value) || 0 })}
              />
            </div>
          </>
        );

      case 'filterResults':
        return (
          <div className={styles.fieldGrow}>
            <Input
              label="Keep only results where"
              value={consequence.filterExpression}
              invalid={invalid}
              placeholder="e.g. Category:coffee, Tags:brewing"
              onChange={(event) => onChange({ filterExpression: event.target.value })}
            />
          </div>
        );

      case 'removeWord':
        return (
          <div className={styles.fieldGrow}>
            <Input
              label="Word"
              value={consequence.word}
              invalid={invalid}
              placeholder="e.g. cheap"
              onChange={(event) => onChange({ word: event.target.value })}
            />
          </div>
        );

      case 'replaceWord':
        return (
          <>
            <div className={styles.fieldGrow}>
              <Input
                label="Replace"
                value={consequence.word}
                invalid={invalid}
                placeholder="e.g. mill"
                onChange={(event) => onChange({ word: event.target.value })}
              />
            </div>
            <div className={styles.fieldGrow}>
              <Input
                label="…with"
                value={consequence.replacement}
                invalid={invalid}
                placeholder="e.g. grinder"
                onChange={(event) => onChange({ replacement: event.target.value })}
              />
            </div>
          </>
        );

      case 'replaceQuery':
        return (
          <div className={styles.fieldGrow}>
            <Input
              label="Search instead for"
              value={consequence.query}
              invalid={invalid}
              placeholder="e.g. hand grinder"
              onChange={(event) => onChange({ query: event.target.value })}
            />
          </div>
        );

      case 'redirect':
        return (
          <div className={styles.fieldGrow}>
            <Input
              label="Send the visitor to"
              value={consequence.url}
              invalid={invalid}
              placeholder="/campaigns/grinder-week"
              onChange={(event) => onChange({ url: event.target.value })}
            />
          </div>
        );

      case 'customData':
        return (
          <div className={`${styles.fieldFull} ${styles.jsonArea}`}>
            <TextArea
              label="JSON"
              value={consequence.json}
              invalid={invalid}
              minRows={4}
              onChange={(event) => onChange({ json: event.target.value })}
            />
          </div>
        );

      default:
        return null;
    }
  };

  return (
    <Card>
      <div className={styles.cardHeader}>
        <span className={styles.cardTitle}>{consequenceLabels[consequence.type].label}</span>
        <Button
          label="Remove"
          title={`Remove ${consequenceLabels[consequence.type].label}`}
          icon="xp-bin"
          destructive
          color={ButtonColor.Quinary}
          size={ButtonSize.XS}
          onClick={onRemove}
        />
      </div>
      <div className={styles.fields}>{fields()}</div>
      {note === undefined ? null : <p style={muted}>{note}</p>}
      {errors.map((message) => (
        <p key={message} className={styles.error} role="alert">
          {message}
        </p>
      ))}
    </Card>
  );
};
