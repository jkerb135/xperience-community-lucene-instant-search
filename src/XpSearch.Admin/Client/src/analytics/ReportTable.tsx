import { useState, type ReactNode } from 'react';
import {
  Pagination,
  Card,
  CellType,
  ColumnContentType,
  Colors,
  Table,
  Tag,
} from '@kentico/xperience-admin-components';
import type { ComponentCell, TableColumn, TableRow } from '@kentico/xperience-admin-components';

import { muted } from '../theme';

import styles from './AnalyticsDashboard.module.scss';

/*
 * The report card the analytics dashboard repeats four times: a Card holding a stock Table, with an
 * optional count Tag next to the card title, an optional note, the pager row and an optional hint.
 * Built to docs/internal/design/Analytics.dc.html; see docs/adr/0020-admin-page-design.md.
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

/**
 * A cell that renders our own node: the board's bold query, its right-aligned numbers and its row
 * action. ComponentCell renders `<cell.component />`, so a cell holds a component, not an element.
 */
export const node = (columnName: string, render: () => ReactNode): ComponentCell => ({
  type: CellType.Component,
  columnName,
  component: () => <div className={`${styles.cell} ${styles.cellStart}`}>{render()}</div>,
});

/** The same, right-aligned: every number column of the board reads right. */
export const numberNode = (columnName: string, value: string): ComponentCell => ({
  type: CellType.Component,
  columnName,
  component: () => <div className={`${styles.cell} ${styles.cellEnd}`}>{value}</div>,
});

/*
 * Paging is the stock Pagination component (owner decision 2026-08-25). Its previous/next controls
 * are icon-only Buttons without a `label`, so they announce as "button" (the HW-10 #5 pattern in
 * the platform's own component); the aria-live count line next to it carries the page context.
 */
export const TablePager = ({
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
  <div className={styles.pager}>
    <p style={muted} aria-live="polite">{`Page ${page} of ${totalPages} · ${total} rows`}</p>
    <Pagination selectedPage={page} totalPages={totalPages} onPageChange={onChange} />
  </div>
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
  /** The per-table class that right-aligns this report's number captions. */
  readonly tableClassName?: string;
}

export const ReportTable = ({
  headline,
  count,
  note,
  columns,
  rows,
  pageSize,
  emptyText,
  footer,
  hint,
  tableClassName,
}: ReportTableProps) => {
  const [page, setPage] = useState(1);

  const totalPages = Math.max(1, Math.ceil(rows.length / pageSize));
  // The rows can shrink under a page the user is standing on, so the state is clamped, not trusted.
  const current = Math.min(page, totalPages);

  return (
    <div className={styles.card}>
      <Card
        headline={
          <div className={`${styles.cardHeader} ${styles.middle}`}>
            <div className={styles.titleRow}>
              <span>{headline}</span>
              {count === undefined ? null : (
                <Tag label={count} readOnly background={{ color: Colors.BackgroundTagSkyBlue }} />
              )}
            </div>
            {note === undefined ? null : <p style={muted}>{note}</p>}
          </div>
        }
        footer={footer}
      >
        {rows.length === 0 ? (
          <p style={muted}>{emptyText}</p>
        ) : (
          <div className={tableClassName === undefined ? styles.table : `${styles.table} ${tableClassName}`}>
            <Table columns={columns} rows={rows.slice((current - 1) * pageSize, current * pageSize)} isHeaderVisible />
          </div>
        )}
        {totalPages > 1 ? (
          <TablePager page={current} totalPages={totalPages} total={rows.length} onChange={setPage} />
        ) : null}
        {hint === undefined ? null : <p style={muted}>{hint}</p>}
      </Card>
    </div>
  );
};
