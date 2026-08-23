import { ReactElement, useState } from 'react';
import {
  Button,
  ButtonColor,
  Callout,
  CalloutPlacementType,
  CalloutType,
  Card,
  CellType,
  ComponentCell,
  Colors,
  ColumnContentType,
  Dialog,
  Headline,
  HeadlineSize,
  Spinner,
  StringCell,
  Table,
  TableColumn,
  TableRow,
  Tag,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import styles from './IndexStatusTemplate.module.css';

/*
 * Client template of the index status page (spec 10.8). Registered as
 * "@yourco/xperience-search-admin/IndexStatus"; the back end is
 * XpSearch.Admin.UIPages.IndexStatusPage.
 * https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages
 */

interface IndexStatusProps {
  readonly indexName: string;
}

interface SourceCount {
  readonly source: string;
  readonly kind: string;
  readonly count: number;
  readonly share: number;
}

interface IngestionEntry {
  readonly timestamp: string;
  readonly source: string;
  readonly operation: string;
  readonly count: number;
  readonly succeeded: boolean;
  readonly message: string;
}

interface Status {
  readonly indexName: string;
  readonly health: string;
  readonly documents: number;
  readonly sources: number;
  readonly failedWrites: number;
  readonly lastWrite: string;
  readonly bySource: SourceCount[];
  readonly recentIngestion: IngestionEntry[];
  readonly rebuildStartedAt: string;
  readonly error: string;
}

const Commands = {
  Load: 'Load',
  Rebuild: 'Rebuild',
};

/** Enough distinct tag backgrounds for the sources one index realistically has; wraps after that. */
const sourceColors = [
  Colors.BackgroundTagXperienceViolet,
  Colors.BackgroundTagSkyBlue,
  Colors.BackgroundTagNeonGreen,
  Colors.BackgroundTagYellow,
  Colors.BackgroundTagRose,
  Colors.BackgroundTagWarmGrey,
];

const column = (name: string, caption: string, minWidth: number, maxWidth: number, contentType = ColumnContentType.Text): TableColumn => ({
  name,
  caption,
  visible: true,
  minWidth,
  maxWidth,
  contentType,
  sortable: false,
  searchable: false,
});

const text = (columnName: string, value: string): StringCell => ({ type: CellType.String, columnName, value });

const node = (columnName: string, render: () => ReactElement): ComponentCell => ({
  type: CellType.Component,
  columnName,
  // ComponentCell renders <cell.component />, so the cell holds a component, not an element.
  component: render,
});

const percent = (share: number): string => `${Math.round(share * 1000) / 10}%`;

const sourceColumns: TableColumn[] = [
  column('source', 'Source', 20, 40),
  column('documents', 'Documents', 12, 16),
  column('share', 'Share', 10, 14),
];

const ingestionColumns: TableColumn[] = [
  column('timestamp', 'Timestamp', 20, 24),
  column('source', 'Source', 12, 18),
  column('operation', 'Operation', 12, 16, ColumnContentType.Component),
  column('count', 'Count', 8, 10),
  column('result', 'Result', 12, 14, ColumnContentType.Component),
  column('message', 'Message', 24, 60),
];

/** The sources that only ever appear in failed log entries have nothing in the index at all. */
const missingSources = (status: Status): string[] => {
  const indexed = new Set(status.bySource.map((row) => row.source));

  return [
    ...new Set(
      status.recentIngestion
        .filter((entry) => !entry.succeeded)
        .map((entry) => entry.source)
        .filter((source) => !indexed.has(source)),
    ),
  ];
};

const failureReport = (status: Status): string =>
  [
    `Index\t${status.indexName}`,
    `Failed writes\t${status.failedWrites}`,
    'Timestamp\tSource\tOperation\tCount\tMessage',
    ...status.recentIngestion
      .filter((entry) => !entry.succeeded)
      .map((entry) => [entry.timestamp, entry.source, entry.operation, entry.count, entry.message].join('\t')),
  ].join('\n');

export const IndexStatusTemplate = ({ indexName }: IndexStatusProps) => {
  const [status, setStatus] = useState<Status | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [confirming, setConfirming] = useState(false);
  const [triggering, setTriggering] = useState(false);
  const [rebuildStartedAt, setRebuildStartedAt] = useState('');
  const [copied, setCopied] = useState(false);

  const { execute: load } = usePageCommand<Status>(
    Commands.Load,
    {
      executeOnMount: true,
      after: (response) => {
        setLoading(false);
        setStatus(response);
        setRebuildStartedAt(response?.rebuildStartedAt ?? '');
      },
    },
    [],
  );

  const { execute: rebuild } = usePageCommand<Status>(Commands.Rebuild, {
    after: (response) => {
      setTriggering(false);
      setConfirming(false);

      if (response) {
        setStatus(response);
        setRebuildStartedAt(response.rebuildStartedAt);
      }
    },
  });

  const reload = () => {
    setLoading(true);
    void load();
  };

  const degraded = status?.health === 'Degraded';
  const rebuilding = rebuildStartedAt !== '';

  const copyFailures = () => {
    if (!status) {
      return;
    }

    void navigator.clipboard.writeText(failureReport(status)).then(() => setCopied(true));
  };

  const rebuildButton = (
    <Button
      label="Rebuild index"
      destructive
      color={ButtonColor.Primary}
      onClick={() => setConfirming(true)}
      disabled={loading || rebuilding}
    />
  );

  const figure = (label: string, value: string) => (
    <div key={label}>
      <div className={styles.figureLabel}>{label}</div>
      <div className={styles.figureValue}>{value}</div>
    </div>
  );

  const sourceRows = (current: Status): TableRow[] =>
    current.bySource.map((row, index) => ({
      identifier: row.source,
      disabled: false,
      cells: [
        node('source', () => (
          <span>
            <span className={styles.swatch} style={{ background: sourceColors[index % sourceColors.length] }} />
            <span>{row.source}</span>
            <span className={styles.sourceKind}> — {row.kind}</span>
          </span>
        )),
        text('documents', row.count.toLocaleString()),
        text('share', percent(row.share)),
      ],
    }));

  const ingestionRows = (current: Status): TableRow[] =>
    current.recentIngestion.map((entry, index) => ({
      identifier: `${entry.timestamp}-${index}`,
      disabled: false,
      // The invalid treatment is colour; the "Failed" tag next to it carries the same meaning as text.
      isInvalid: !entry.succeeded,
      cells: [
        text('timestamp', entry.timestamp),
        text('source', entry.source),
        node('operation', () => <Tag label={entry.operation} readOnly />),
        text('count', entry.count.toLocaleString()),
        node('result', () => (
          <Tag
            label={entry.succeeded ? 'Succeeded' : 'Failed'}
            readOnly
            background={{ color: entry.succeeded ? Colors.SuccessBackgroundLowEmphasis : Colors.AlertBackgroundLowEmphasis }}
          />
        )),
        text('message', entry.message),
      ],
    }));

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <div>
          <Headline size={HeadlineSize.L}>Status</Headline>
          <p className={styles.subtitle}>
            Index <strong>{indexName}</strong> · Lucene
          </p>
        </div>
        {rebuilding ? (
          <span className={styles.inProgress}>
            <Spinner />
            <Tag label="Rebuilding" readOnly />
          </span>
        ) : (
          rebuildButton
        )}
      </div>

      <div aria-live="polite">
        {loading ? <Spinner /> : null}

        {!loading && status && status.error !== '' ? (
          <Callout
            type={CalloutType.FriendlyWarning}
            placement={CalloutPlacementType.OnDesk}
            subheadline="Friendly warning"
            headline="The status of this index could not be read"
            actionButton={<Button label="Load again" onClick={reload} />}
          >
            {status.error}
          </Callout>
        ) : null}

        {!loading && status && status.error === '' ? (
          <>
            <Card>
              <div className={styles.figures}>
                {rebuilding ? (
                  <span className={styles.inProgress}>
                    <Spinner />
                    <Tag label="Rebuild in progress" readOnly />
                  </span>
                ) : (
                  <Tag
                    label={degraded ? 'Degraded' : 'Healthy'}
                    readOnly
                    background={{ color: degraded ? Colors.AlertBackgroundLowEmphasis : Colors.SuccessBackgroundLowEmphasis }}
                  />
                )}
                {figure('Documents', status.documents.toLocaleString())}
                {degraded ? figure('Failed writes', status.failedWrites.toLocaleString()) : figure('Sources', status.sources.toLocaleString())}
                {rebuilding
                  ? figure('Started', rebuildStartedAt)
                  : figure('Last external write', status.lastWrite === '' ? 'never' : status.lastWrite)}
              </div>
              <p className={styles.note}>
                {rebuilding
                  ? 'Search results are incomplete until the rebuild finishes. External pushes received during the rebuild are queued and applied afterwards.'
                  : 'All queued external writes reached Lucene. Counts are eventually consistent while work is queued — a short lag is normal and is not reported as degraded.'}
              </p>
            </Card>

            {degraded && !rebuilding ? (
              <Callout
                type={CalloutType.FriendlyWarning}
                placement={CalloutPlacementType.OnDesk}
                subheadline="Friendly warning"
                headline={`${status.failedWrites} queued write(s) never reached Lucene`}
                maxWidth="100%"
                actionButton={
                  <span className={styles.actions}>
                    <Button label={copied ? 'Copied' : 'Copy failure details'} onClick={copyFailures} />
                    {rebuildButton}
                  </span>
                }
              >
                The index is still searchable, but documents from the sources listed below are missing. Read the failed entries below for
                the reason, ask the source system to push the batch again, and rebuild the index if you cannot tell which documents were
                lost.
              </Callout>
            ) : null}

            <div className={styles.columns}>
              <Card headline="Documents by source">
                <div className={styles.bar}>
                  {status.bySource.map((row, index) => (
                    <div
                      key={row.source}
                      className={styles.segment}
                      style={{ width: `${row.share * 100}%`, background: sourceColors[index % sourceColors.length] }}
                    />
                  ))}
                </div>
                <Table columns={sourceColumns} rows={sourceRows(status)} />
                {missingSources(status).map((source) => (
                  <p key={source} className={styles.alertNote}>
                    {source} has never written successfully — its documents are absent from the index.
                  </p>
                ))}
              </Card>

              <div>
                {degraded ? (
                  <Callout
                    type={CalloutType.QuickTip}
                    placement={CalloutPlacementType.OnDesk}
                    subheadline="Quick tip"
                    headline="Degraded is not the same as lagging"
                    maxWidth="100%"
                  >
                    A queue with work in it is normal: counts catch up on their own and health stays Healthy. Degraded means a queued write
                    was rejected by Lucene and will not be retried on its own.
                  </Callout>
                ) : (
                  <Callout
                    type={CalloutType.QuickTip}
                    placement={CalloutPlacementType.OnDesk}
                    subheadline="Quick tip"
                    headline="Where these documents come from"
                    maxWidth="100%"
                  >
                    <strong>xperience</strong> is content indexed by the CMS. <strong>pim</strong> and any other source are external systems
                    pushing documents through the ingestion API, so their counts change without a content update in Xperience.
                  </Callout>
                )}
              </div>
            </div>

            <Card
              headline="Recent ingestion"
              description={degraded ? `${status.failedWrites} failed entries first` : 'Last 10 entries'}
            >
              <div className={styles.ingestion}>
                <Table columns={ingestionColumns} rows={ingestionRows(status)} />
              </div>
            </Card>
          </>
        ) : null}
      </div>

      <Dialog
        isOpen={confirming}
        isDismissable
        actionInProgress={triggering}
        headline="Rebuild the index?"
        headerCloseButton={{ tooltipText: 'Close' }}
        onClose={() => setConfirming(false)}
        cancelAction={{ label: 'Cancel', disabled: triggering, onClick: () => setConfirming(false) }}
        confirmAction={{
          label: 'Rebuild',
          destructive: true,
          disabled: triggering,
          inProgress: triggering,
          onClick: () => {
            setTriggering(true);
            void rebuild();
          },
        }}
      >
        The index is emptied and written again. Search results are incomplete until it finishes.
      </Dialog>
    </div>
  );
};
