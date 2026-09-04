import { useState } from 'react';
import {
  Card,
  CellType,
  Colors,
  ColumnContentType,
  Headline,
  HeadlineSize,
  Inline,
  Spacing,
  Table,
} from '@kentico/xperience-admin-components';
import type { StringCell, TableColumn, TableRow } from '@kentico/xperience-admin-components';

import { muted } from '../theme';
import { TablePager } from './ReportTable';

import './ReportTable.scss';
import styles from './AnalyticsDashboard.module.scss';

/*
 * Searches and zero-result searches over time. The design system exposes no line chart - only
 * FunnelChart - and a chart library is not worth a dependency for two series, so this is plain SVG
 * with a table fallback for screen readers. See docs/adr/0016-admin-client.md.
 */

export interface VolumePoint {
  readonly day: string;
  readonly volume: number;
  readonly zeroResultVolume: number;
}

interface VolumeChartProps {
  readonly points: VolumePoint[];
  /** Formats a yyyy-mm-dd day for the axis labels. */
  readonly formatDay: (day: string) => string;
  /** How many rows one page of the "Show the numbers" table holds, as for the report tables. */
  readonly pageSize: number;
}

const width = 1000;
const height = 190;
const searchesColor = Colors.Product;
const zeroColor = Colors.AlertIcon;

/** The polyline through one series, scaled to the chart box. A single point renders as a flat line. */
const path = (values: number[], peak: number): string =>
  values
    .map((value, index) => {
      const x = values.length === 1 ? 0 : (index / (values.length - 1)) * width;
      const y = height - (value / peak) * height;

      return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)} ${y.toFixed(1)}`;
    })
    .join(' ')
    .concat(values.length === 1 ? ` L${width} ${(height - (values[0] / peak) * height).toFixed(1)}` : '');

const numberColumn = (name: string, caption: string): TableColumn => ({
  name,
  caption,
  visible: true,
  minWidth: 10,
  maxWidth: 0,
  contentType: ColumnContentType.Text,
  sortable: false,
  searchable: false,
});

const numberColumns: TableColumn[] = [
  numberColumn('day', 'Date'),
  numberColumn('volume', 'Searches'),
  numberColumn('zeroResultVolume', 'Zero-result searches'),
];

const numberCell = (columnName: string, value: string): StringCell => ({ type: CellType.String, columnName, value });

const numberRow =
  (formatDay: (day: string) => string) =>
  (point: VolumePoint): TableRow => ({
    identifier: point.day,
    disabled: false,
    cells: [
      numberCell('day', formatDay(point.day)),
      numberCell('volume', String(point.volume)),
      numberCell('zeroResultVolume', String(point.zeroResultVolume)),
    ],
  });

const Legend = ({ color, label }: { readonly color: string; readonly label: string }) => (
  <span className={styles.legend} style={muted}>
    <span aria-hidden="true" className={styles.swatch} style={{ background: color }} />
    {label}
  </span>
);

export const VolumeChart = ({ points, formatDay, pageSize }: VolumeChartProps) => {
  const [page, setPage] = useState(1);

  const peak = Math.max(...points.map((point) => point.volume), 1);
  const labels = points.filter((_, index) => index % Math.ceil(points.length / 6) === 0);
  const totalPages = Math.max(1, Math.ceil(points.length / pageSize));
  // As in ReportTable: the series can shrink under the page the user is standing on.
  const current = Math.min(page, totalPages);

  return (
    <Card
      headline={<Headline size={HeadlineSize.S}>Searches over time</Headline>}
      description={
        <Inline spacing={Spacing.L}>
          <Legend color={searchesColor} label="Searches" />
          <Legend color={zeroColor} label="Zero-result searches" />
        </Inline>
      }
    >
      <svg
        viewBox={`0 -10 ${width} ${height + 20}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`Searches per day, peaking at ${peak}. The same numbers are in the table below the chart.`}
        className={styles.plot}
      >
        {[0, 60, 120].map((y) => (
          <line key={y} x1="0" y1={y} x2={width} y2={y} stroke={Colors.DividerDefault} strokeWidth="1" strokeDasharray="3 4" />
        ))}
        <line x1="0" y1={height} x2={width} y2={height} stroke={Colors.BorderDefault} strokeWidth="1" />
        <path d={path(points.map((point) => point.volume), peak)} fill="none" stroke={searchesColor} strokeWidth="2" />
        <path d={path(points.map((point) => point.zeroResultVolume), peak)} fill="none" stroke={zeroColor} strokeWidth="2" />
      </svg>
      <div className={styles.axis}>
        {labels.map((point) => (
          <span key={point.day} style={muted}>
            {formatDay(point.day)}
          </span>
        ))}
      </div>
      <details>
        <summary>Show the numbers</summary>
        <Table
          columns={numberColumns}
          rows={points.slice((current - 1) * pageSize, current * pageSize).map(numberRow(formatDay))}
          isHeaderVisible
        />
        {totalPages > 1 ? (
          <TablePager page={current} totalPages={totalPages} total={points.length} onChange={setPage} />
        ) : null}
      </details>
    </Card>
  );
};
