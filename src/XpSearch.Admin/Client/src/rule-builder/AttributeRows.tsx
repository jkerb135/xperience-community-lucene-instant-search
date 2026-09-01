import { useEffect, useState } from 'react';
import {
  Button,
  ButtonColor,
  ButtonSize,
  Input,
  MenuItem,
  Select,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { muted } from '../theme';
import { composeExpression, parseExpression } from './expression';
import type { Filter } from './model';
import styles from './RuleBuilderTemplate.module.scss';

/*
 * The attribute + value picker of design canvas 5h, used by the Filter results panel, the Boost
 * "matching" variant and the condition Filters rows - the same anatomy in all three. The attribute
 * comes from the index schema's facetable fields; the value from a facet-only query, so the list is
 * what the index really holds, with counts. An attribute the index does not facet falls back to a
 * plain text value.
 *
 * "Edit as text" swaps the rows for the raw expression the storage keeps, and back; the two are the
 * same string, parsed and composed by ./expression.
 */

interface AttributeValue {
  readonly value: string;
  readonly label: string;
  readonly count: number;
}

interface AttributeValuesResult {
  readonly values: AttributeValue[];
  readonly error: string;
}

interface AttributeRowsProps {
  readonly rows: Filter[];
  /** The facetable attributes of the index, as the page was loaded with. */
  readonly attributes: string[];
  readonly onChange: (rows: Filter[]) => void;
}

/** The value control of one row: a drop-down of real values, or text for a non-facetable attribute. */
const ValueField = ({
  attribute,
  value,
  facetable,
  labelled,
  onChange,
}: {
  attribute: string;
  value: string;
  facetable: boolean;
  labelled: boolean;
  onChange: (value: string) => void;
}) => {
  const [values, setValues] = useState<AttributeValue[]>([]);

  const { execute: load } = usePageCommand<AttributeValuesResult, { attribute: string }>('GetAttributeValues', {
    after: (response) => setValues(response?.values ?? []),
  });

  useEffect(() => {
    if (!facetable || attribute === '') {
      setValues([]);

      return;
    }

    void load({ attribute });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attribute, facetable]);

  if (!facetable || values.length === 0) {
    return (
      <Input
        label={labelled ? 'Value' : undefined}
        value={value}
        placeholder={facetable ? 'Loading the index values…' : 'Type the value'}
        onChange={(event) => onChange(event.target.value)}
      />
    );
  }

  return (
    <Select label={labelled ? 'Value' : undefined} value={value} onChange={(picked) => onChange(picked ?? '')}>
      {/* A value the index no longer holds must still be shown, or opening the panel would clear it. */}
      {value !== '' && !values.some((candidate) => candidate.value === value) ? (
        <MenuItem primaryLabel={value} secondaryLabel="not in the index" value={value} />
      ) : null}
      {values.map((candidate) => (
        <MenuItem
          key={candidate.value}
          primaryLabel={candidate.label}
          secondaryLabel={String(candidate.count)}
          value={candidate.value}
        />
      ))}
    </Select>
  );
};

export const AttributeRows = ({ rows, attributes, onChange }: AttributeRowsProps) => {
  // Undefined while the rows are being edited; the raw expression while "Edit as text" is on.
  const [text, setText] = useState<string | undefined>(undefined);

  const change = (at: number, values: Partial<Filter>) =>
    onChange(rows.map((row, index) => (index === at ? { ...row, ...values } : row)));

  if (text !== undefined) {
    return (
      <div className={styles.toggleFields}>
        <Input
          label="Expression"
          value={text}
          placeholder="e.g. Category:coffee, Tags:brewing"
          explanationText="Comma-separated attribute:value pairs; all of them must hold."
          onChange={(event) => {
            setText(event.target.value);
            onChange(parseExpression(event.target.value));
          }}
        />
        <div>
          <Button
            label="Back to rows"
            color={ButtonColor.Quinary}
            size={ButtonSize.XS}
            onClick={() => setText(undefined)}
          />
        </div>
      </div>
    );
  }

  return (
    <div className={styles.toggleFields}>
      {rows.map((row, at) => (
        <div key={at} className={styles.filterRow}>
          <div className={styles.fieldGrow}>
            {attributes.length === 0 ? (
              <Input
                label={at === 0 ? 'Attribute' : undefined}
                value={row.attribute}
                placeholder="e.g. contentType"
                onChange={(event) => change(at, { attribute: event.target.value })}
              />
            ) : (
              <Select
                label={at === 0 ? 'Attribute' : undefined}
                value={row.attribute}
                onChange={(picked) => change(at, { attribute: picked ?? '', value: '' })}
              >
                <MenuItem primaryLabel="Add attribute" value="" />
                {/* An attribute a saved rule names that the index no longer facets stays selectable. */}
                {row.attribute !== '' && !attributes.includes(row.attribute) ? (
                  <MenuItem primaryLabel={row.attribute} secondaryLabel="not facetable" value={row.attribute} />
                ) : null}
                {attributes.map((attribute) => (
                  <MenuItem key={attribute} primaryLabel={attribute} value={attribute} />
                ))}
              </Select>
            )}
          </div>
          <span className={styles.filterIs}>is</span>
          <div className={styles.fieldGrow}>
            <ValueField
              attribute={row.attribute}
              value={row.value}
              facetable={attributes.includes(row.attribute)}
              labelled={at === 0}
              onChange={(value) => change(at, { value })}
            />
          </div>
          <Button
            label="Remove"
            title={`Remove row ${String(at + 1)}`}
            icon="xp-times"
            color={ButtonColor.Quinary}
            size={ButtonSize.XS}
            onClick={() => onChange(rows.filter((_, index) => index !== at))}
          />
        </div>
      ))}

      <div className={styles.rowActions}>
        <Button
          label="Add row"
          color={ButtonColor.Tertiary}
          size={ButtonSize.XS}
          onClick={() => onChange([...rows, { attribute: '', value: '' }])}
        />
        <Button
          label="Edit as text"
          color={ButtonColor.Quinary}
          size={ButtonSize.XS}
          onClick={() => setText(composeExpression(rows))}
        />
      </div>

      {rows.length === 0 ? <p style={muted}>No rows yet — add one, or write the expression as text.</p> : null}
    </div>
  );
};
