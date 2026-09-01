import { useState, type ReactNode } from 'react';
import {
  Pagination,
  Card,
  CellType,
  ColumnContentType,
  Colors,
  Headline,
  HeadlineSize,
  Spacing,
  Table,
  Tag, Box
} from '@kentico/xperience-admin-components';
import type { TableColumn, TableRow } from '@kentico/xperience-admin-components';

import {muted,  flexRow} from '../theme';

import "./ReportTable.scss";

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

/*
 * Paging is the stock Pagination component (owner decision 2026-08-25). Its previous/next controls
 * are icon-only Buttons without a `label`, so they announce as "button" (the HW-10 #5 pattern in
 * the platform's own component); the aria-live count line next to it carries the page context.
 */
const TablePager = ({
  page,
  totalPages,
  total,
  onChange,
}: {
  readonly page: number;
  readonly totalPages: number;
  readonly total: number;
  readonly onChange: (page: number) => void;
}) => (
  <Box spacing={Spacing.S} className={"pager"}>
    <p style={muted} aria-live="polite">{`Page ${page} of ${totalPages} · ${total} rows`}</p>
    <Pagination selectedPage={page} totalPages={totalPages} onPageChange={onChange} />
  </Box>
);

interface ReportTableProps {
  readonly headline: string;
  /** Small count chip next to the headline, for example "309 searches · 5 queries". */
  readonly count?: string;
  /** Right-aligned note in the headline row. */
  readonly note?: string;
  readonly columns: TableColumn[];
  readonly rows: TableRow[];
  /** How many rows one page holds. The pager is hidden when every row fits on one. */
  readonly pageSize: number;
  readonly emptyText: string;
  readonly footer?: ReactNode;
  /** Line of guidance under the table, for example what the row action does. */
  readonly hint?: string;
}

export const ReportTable = ({ headline, count, note, columns, rows, pageSize, emptyText, footer, hint }: ReportTableProps) => {
  const [page, setPage] = useState(1);

  const totalPages = Math.max(1, Math.ceil(rows.length / pageSize));
  // The rows can shrink under a page the user is standing on, so the state is clamped, not trusted.
  const current = Math.min(page, totalPages);

  return (
    <Card
      headline={
        <div style={flexRow}>
          <Headline size={HeadlineSize.S}>{headline}</Headline>
          {count === undefined ? null : <Tag label={count} readOnly background={{ color: Colors.InfoBackgroundHighEmphasis }} />}
        </div>
      }
      description={note === undefined ? undefined : <p style={muted}>{note}</p>}
      footer={footer}
    >
      {rows.length === 0 ? (
        <p style={muted}>{emptyText}</p>
      ) : (
        <Table columns={columns} rows={rows.slice((current - 1) * pageSize, current * pageSize)} isHeaderVisible />
      )}
      {totalPages > 1 ? (
        <TablePager page={current} totalPages={totalPages} total={rows.length} onChange={setPage} />
      ) : null}
      {hint === undefined ? null : <p style={muted}>{hint}</p>}
    </Card>
  );
};
