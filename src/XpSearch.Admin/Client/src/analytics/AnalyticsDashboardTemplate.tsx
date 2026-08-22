import { useState } from 'react';
import {
  Button,
  ButtonColor,
  ButtonSize,
  ButtonType,
  Headline,
  HeadlineSize,
  Input,
  MenuItem,
  Select,
  Spinner,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { Column, ReportTable } from './ReportTable';
import { VolumeChart, VolumePoint } from './VolumeChart';

/*
 * Client template of the analytics dashboard (spec 9.3). Registered as
 * "@yourco/xperience-search-admin/AnalyticsDashboard"; the back end is
 * XpSearch.Admin.UIPages.Analytics.AnalyticsDashboardPage.
 * https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages
 */

interface AnalyticsDashboardProps {
  readonly indexNames: string[];
  /** True when the page hangs under one index, so the index is shown rather than chosen. */
  readonly indexLocked: boolean;
  readonly selectedIndexName: string;
  readonly today: string;
}

interface QueryRow {
  readonly query: string;
  readonly volume: number;
  readonly p95ProcessingTimeMs: number;
}

interface ZeroResultRow {
  readonly query: string;
  readonly volume: number;
  readonly lastSeen: string;
}

interface ClickThroughRow {
  readonly query: string;
  readonly volume: number;
  readonly clicks: number;
  readonly clickThroughRate: number;
  readonly averageClickedPosition: number | null;
}

interface Report {
  readonly topQueries: QueryRow[];
  readonly zeroResultQueries: ZeroResultRow[];
  readonly clickThrough: ClickThroughRow[];
  readonly averageClickedPosition: number | null;
  readonly volumeOverTime: VolumePoint[];
  readonly slowestQueries: QueryRow[];
  readonly totalSearches: number;
  readonly error: string;
}

interface LoadData {
  readonly from: string;
  readonly to: string;
  readonly limit: number;
}

interface CreateRuleData {
  readonly query: string;
}

const Commands = {
  Load: 'Load',
  CreateRule: 'CreateRule',
};

const presets = [7, 30, 90];

const shiftDays = (day: string, days: number): string => {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() - days);

  return date.toISOString().slice(0, 10);
};

const percent = (value: number): string => `${Math.round(value * 1000) / 10}%`;

const position = (value: number | null): string => (value === null ? '—' : value.toFixed(1));

const allIndexes = '*';

export const AnalyticsDashboardTemplate = ({ indexNames, selectedIndexName, indexLocked, today }: AnalyticsDashboardProps) => {
  const [indexName, setIndexName] = useState(selectedIndexName);
  const [from, setFrom] = useState(shiftDays(today, 29));
  const [to, setTo] = useState(today);
  const [report, setReport] = useState<Report | undefined>(undefined);
  const [loading, setLoading] = useState(true);

  const { execute: load } = usePageCommand<Report, LoadData>(
    Commands.Load,
    {
      data: { from, to, limit: 20 },
      executeOnMount: true,
      after: (response) => {
        setLoading(false);
        setReport(response);
      },
    },
    [],
  );

  const { execute: createRule } = usePageCommand<void, CreateRuleData>(Commands.CreateRule);

  const reload = (nextFrom: string, nextTo: string) => {
    setFrom(nextFrom);
    setTo(nextTo);
    setLoading(true);
    void load({ from: nextFrom, to: nextTo, limit: 20 });
  };

  const topColumns: Array<Column<QueryRow>> = [
    { key: 'query', caption: 'Query', render: (row) => row.query },
    { key: 'volume', caption: 'Searches', numeric: true, render: (row) => row.volume },
  ];

  const slowColumns: Array<Column<QueryRow>> = [
    { key: 'query', caption: 'Query', render: (row) => row.query },
    { key: 'volume', caption: 'Searches', numeric: true, render: (row) => row.volume },
    { key: 'p95', caption: '95th percentile', numeric: true, render: (row) => `${row.p95ProcessingTimeMs} ms` },
  ];

  const zeroColumns: Array<Column<ZeroResultRow>> = [
    { key: 'query', caption: 'Query', render: (row) => row.query },
    { key: 'volume', caption: 'Searches', numeric: true, render: (row) => row.volume },
    { key: 'lastSeen', caption: 'Last seen', render: (row) => row.lastSeen },
    {
      key: 'action',
      caption: 'Fix',
      render: (row) => (
        <Button
          label="Create rule"
          size={ButtonSize.XS}
          title={`Create a rule for "${row.query}"`}
          onClick={() => {
            void createRule({ query: row.query });
          }}
        />
      ),
    },
  ];

  const clickColumns: Array<Column<ClickThroughRow>> = [
    { key: 'query', caption: 'Query', render: (row) => row.query },
    { key: 'volume', caption: 'Searches', numeric: true, render: (row) => row.volume },
    { key: 'clicks', caption: 'Clicks', numeric: true, render: (row) => row.clicks },
    { key: 'ctr', caption: 'Click-through', numeric: true, render: (row) => percent(row.clickThroughRate) },
    { key: 'avg', caption: 'Avg. position', numeric: true, render: (row) => position(row.averageClickedPosition) },
  ];

  return (
    <div style={{ padding: '16px' }}>
      <Headline size={HeadlineSize.L}>Search analytics</Headline>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          reload(from, to);
        }}
        style={{ display: 'flex', flexWrap: 'wrap', gap: '16px', alignItems: 'flex-end' }}
      >
        <div style={{ minWidth: '240px' }}>
          {indexLocked ? (
            <p style={{ margin: 0 }}>
              Index: <strong>{indexName}</strong>
            </p>
          ) : (
            <Select
              label="Index"
              value={indexName === '' ? allIndexes : indexName}
              onChange={(value) => setIndexName(value === allIndexes || value === undefined ? '' : value)}
            >
              <MenuItem primaryLabel="Every index" value={allIndexes} />
              {indexNames.map((name) => (
                <MenuItem key={name} primaryLabel={name} value={name} />
              ))}
            </Select>
          )}
        </div>
        <div style={{ minWidth: '160px' }}>
          <Input label="From" type="text" value={from} placeholder="yyyy-mm-dd" onChange={(event) => setFrom(event.target.value)} />
        </div>
        <div style={{ minWidth: '160px' }}>
          <Input label="To" type="text" value={to} placeholder="yyyy-mm-dd" onChange={(event) => setTo(event.target.value)} />
        </div>
        <Button label="Apply" type={ButtonType.Submit} color={ButtonColor.Primary} size={ButtonSize.M} inProgress={loading} />
        {presets.map((days) => (
          <Button
            key={days}
            label={`Last ${days} days`}
            size={ButtonSize.S}
            onClick={() => reload(shiftDays(today, days - 1), today)}
          />
        ))}
      </form>

      <div aria-live="polite" style={{ marginTop: '24px' }}>
        {loading ? <Spinner /> : null}
        {!loading && report && report.error !== '' ? <p role="alert">{report.error}</p> : null}
        {!loading && report && report.error === '' ? (
          <>
            <p>
              {report.totalSearches} search(es) between {from} and {to}. Average clicked position:{' '}
              {position(report.averageClickedPosition)}.
            </p>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '32px' }}>
              <VolumeChart points={report.volumeOverTime} />
              <ReportTable
                title="Zero-result queries"
                description="What visitors asked for and did not find. Create a rule to fix one."
                columns={zeroColumns}
                rows={report.zeroResultQueries}
                rowKey={(row) => row.query}
                emptyText="Every search in this range found something."
              />
              <ReportTable title="Top queries" columns={topColumns} rows={report.topQueries} rowKey={(row) => row.query} />
              <ReportTable
                title="Click-through rate by query"
                columns={clickColumns}
                rows={report.clickThrough}
                rowKey={(row) => row.query}
              />
              <ReportTable
                title="Slowest queries"
                description="The 95th percentile of the server-side processing time."
                columns={slowColumns}
                rows={report.slowestQueries}
                rowKey={(row) => row.query}
              />
            </div>
          </>
        ) : null}
      </div>
    </div>
  );
};
