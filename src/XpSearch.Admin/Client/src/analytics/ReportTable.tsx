import type { ReactNode } from 'react';
import {
  Card,
  CellType,
  ColumnContentType,
  Colors,
  Headline,
  HeadlineSize,
  Inline,
  Spacing,
  Table,
  Tag,
} from '@kentico/xperience-admin-components';
import type { TableColumn, TableRow } from '@kentico/xperience-admin-components';

import { muted } from '../theme';

/*
 * The report card the analytics dashboard repeats four times: a Card holding a stock Table, with an
 * optional count Tag next to the headline and an optional footer figure. See
 * docs/adr/0020-admin-page-design.md.
 */

/** Builds the descriptor a stock Table column needs, with the defaults every report shares. */
export const column = (
  name: string,
  caption: string,
  options?: { readonly minWidth?: number; readonly maxWidth?: number; readonly contentType?: ColumnContentType },
): TableColumn => ({
  name,
  caption,
  visible: true,
  minWidth: options?.minWidth ?? 10,
  maxWidth: options?.maxWidth ?? 0,
  contentType: options?.contentType ?? ColumnContentType.Text,
  sortable: false,
  searchable: false,
});

/** Builds a read-only string cell. */
export const text = (columnName: string, value: string) => ({
  type: CellType.String,
  columnName,
  value,
});

interface ReportTableProps {
  readonly headline: string;
  /** Small count chip next to the headline, for example "309 searches · 5 queries". */
  readonly count?: string;
  /** Right-aligned note in the headline row. */
  readonly note?: string;
  readonly columns: TableColumn[];
  readonly rows: TableRow[];
  readonly emptyText: string;
  readonly footer?: ReactNode;
  /** Line of guidance under the table, for example what the row action does. */
  readonly hint?: string;
}

export const ReportTable = ({ headline, count, note, columns, rows, emptyText, footer, hint }: ReportTableProps) => (
  <Card
    headline={
      <Inline spacing={Spacing.S}>
        <Headline size={HeadlineSize.S}>{headline}</Headline>
        {count === undefined ? null : <Tag label={count} readOnly background={{ color: Colors.BackgroundTagMajorelleBlue }} />}
      </Inline>
    }
    description={note === undefined ? undefined : <p style={muted}>{note}</p>}
    footer={footer}
  >
    {rows.length === 0 ? <p style={muted}>{emptyText}</p> : <Table columns={columns} rows={rows} isHeaderVisible />}
    {hint === undefined ? null : <p style={muted}>{hint}</p>}
  </Card>
);
