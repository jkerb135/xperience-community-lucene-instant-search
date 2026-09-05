import { useState } from 'react';
import {
  Button,
  ButtonColor,
  Callout,
  CalloutPlacementType,
  CalloutType,
  Card,
  Colors,
  Dialog,
  Input,
  Spacing,
  Spinner,
  Stack,
  Tag,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { muted, stateFigure } from '../theme';

import styles from './ExperimentDetail.module.scss';

/*
 * Client template of the experiment detail page (amendment 2026-08-25). Registered as
 * "@xperience-community/xperience-search/ExperimentDetail"; the back end is
 * XpSearch.Admin.UIPages.Experiments.ExperimentDetailPage.
 *
 * The report shows observed rates and the sample sizes they were observed over - no p-values, no
 * winner, no significance claim. Two rates that differ over a few hundred searches usually differ by
 * chance, and the page must not suggest otherwise.
 */

interface ExperimentDetailProps {
  /** The index the experiment tests. It comes from the URL, so it is shown and never chosen. */
  readonly indexName: string;
  readonly minSplit: number;
  readonly maxSplit: number;
}

interface VariantStats {
  readonly variant: string;
  readonly searches: number;
  readonly zeroResultSearches: number;
  readonly clicks: number;
  readonly averageClickedPosition: number | null;
}

interface Report {
  readonly name: string;
  readonly state: string;
  readonly outcome: string;
  readonly splitPercent: number;
  readonly started: string;
  readonly ended: string;
  readonly a: VariantStats;
  readonly b: VariantStats;
  readonly error: string;
}

interface SplitData {
  readonly splitPercent: number;
}

interface ConcludeData {
  readonly promote: boolean;
}

const Commands = {
  Load: 'Load',
  SetSplit: 'SetSplit',
  Start: 'Start',
  Conclude: 'Conclude',
};

type Confirmation = 'start' | 'promote' | 'discard' | undefined;

const count = (value: number): string => value.toLocaleString();

const rate = (part: number, whole: number): string => (whole === 0 ? '—' : `${((part / whole) * 100).toFixed(1)}%`);

const position = (value: number | null): string => (value === null ? '—' : value.toFixed(1));

const Figure = ({ label, value, hint }: { readonly label: string; readonly value: string; readonly hint: string }) => (
  <div className={styles.figure}>
    <p style={muted}>{label}</p>
    <p style={stateFigure}>{value}</p>
    <p style={muted}>{hint}</p>
  </div>
);

const VariantCard = ({ stats, title }: { readonly stats: VariantStats; readonly title: string }) => (
  <div className={`${styles.variant} ${styles.card}`}>
    <Card
      headline={
        <div className={styles.headerTitle}>
          <span>{title}</span>
          <p style={muted}>{`${count(stats.searches)} searches`}</p>
        </div>
      }
      fullHeight
    >
      <div className={styles.figures}>
        <Figure label="Searches" value={count(stats.searches)} hint="sample size" />
        <Figure
          label="Zero-result rate"
          value={rate(stats.zeroResultSearches, stats.searches)}
          hint={`${count(stats.zeroResultSearches)} found nothing`}
        />
        <Figure label="Click-through rate" value={rate(stats.clicks, stats.searches)} hint={`${count(stats.clicks)} clicks`} />
        <Figure label="Avg clicked position" value={position(stats.averageClickedPosition)} hint="lower is better" />
      </div>
    </Card>
  </div>
);

export const ExperimentDetailTemplate = ({ indexName, minSplit, maxSplit }: ExperimentDetailProps) => {
  const [report, setReport] = useState<Report | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [split, setSplit] = useState('');
  const [confirming, setConfirming] = useState<Confirmation>(undefined);
  const [working, setWorking] = useState(false);

  const received = (response: Report | undefined) => {
    setLoading(false);
    setWorking(false);
    setConfirming(undefined);

    if (response) {
      setReport(response);
      setSplit(String(response.splitPercent));
    }
  };

  const { execute: load } = usePageCommand<Report>(Commands.Load, { executeOnMount: true, after: received }, []);
  const { execute: setSplitCommand } = usePageCommand<Report, SplitData>(Commands.SetSplit, { after: received });
  const { execute: start } = usePageCommand<Report>(Commands.Start, { after: received });
  const { execute: conclude } = usePageCommand<Report, ConcludeData>(Commands.Conclude, { after: received });

  const failed = report !== undefined && report.error !== '';
  const loaded = !loading && report !== undefined && report.error === '';
  const draft = loaded && report.state === 'Draft';
  const running = loaded && report.state === 'Running';
  const concluded = loaded && report.state === 'Concluded';
  const splitPercent = Number(split);
  const splitValid = Number.isInteger(splitPercent) && splitPercent >= minSplit && splitPercent <= maxSplit;

  const run = (action: () => void) => {
    setWorking(true);
    action();
  };

  const confirmations: Record<Exclude<Confirmation, undefined>, { headline: string; label: string; destructive: boolean; body: string; onConfirm: () => void }> = {
    start: {
      headline: 'Start the experiment?',
      label: 'Start',
      destructive: false,
      body: `Every visitor is bucketed immediately: from their next search on, ${report?.splitPercent ?? 0}% of them are answered from variant B and the rest from the live tuning. The split and variant B cannot be changed once the experiment runs.`,
      onConfirm: () => run(() => void start()),
    },
    promote: {
      headline: 'Promote variant B to live?',
      label: 'Promote B',
      destructive: true,
      body: 'The index’s current live rules, synonyms, stopwords and field weights are deleted and replaced by variant B’s. Every visitor is served variant B from then on. This cannot be undone.',
      onConfirm: () => run(() => void conclude({ promote: true })),
    },
    discard: {
      headline: 'Discard variant B?',
      label: 'Discard B',
      destructive: true,
      body: 'Variant B’s rules, synonyms, stopwords and field weights are deleted. The live tuning is left exactly as it is, and every visitor is served it from then on. This cannot be undone.',
      onConfirm: () => run(() => void conclude({ promote: false })),
    },
  };

  const dialog = confirming === undefined ? undefined : confirmations[confirming];

  return (
    <Stack spacing={Spacing.XL}>
      {/*
        * The board's header card: the experiment name and its meta line left, the running actions
        * and the state tags right. The draft controls need an Input, so they stay in the card's
        * body under the header, where the page has always had them.
        */}
      <div className={styles.card}>
        <Card
          headline={
            <div className={styles.cardHeader}>
              <div className={styles.headerTitle}>
                <span>{loaded ? report.name : 'Experiment'}</span>
                <p style={muted}>
                  Index <strong>{indexName}</strong>
                  {loaded ? ` · ${report.splitPercent}% of traffic to variant B` : ''}
                  {loaded && report.started !== '' ? ` · started ${report.started} UTC` : ''}
                  {loaded && report.ended !== '' ? ` · ended ${report.ended} UTC` : ''}
                </p>
              </div>
              {loaded ? (
                <div className={styles.headerActions}>
                  {running ? (
                    <>
                      <Button
                        label="Promote B to live"
                        color={ButtonColor.Primary}
                        destructive
                        disabled={working}
                        onClick={() => setConfirming('promote')}
                      />
                      <Button
                        label="Discard B"
                        color={ButtonColor.Secondary}
                        destructive
                        disabled={working}
                        onClick={() => setConfirming('discard')}
                      />
                    </>
                  ) : null}
                  <div className={styles.tags}>
                    <Tag
                      label={report.state}
                      readOnly
                      background={{ color: running ? Colors.SuccessBackgroundHighEmphasis : Colors.BackgroundTagGrey }}
                    />
                    {concluded ? (
                      <Tag
                        label={`Variant B ${report.outcome.toLowerCase()}`}
                        readOnly
                        background={{
                          color: report.outcome === 'Promoted' ? Colors.SuccessBackgroundHighEmphasis : Colors.BackgroundTagKenticoOrange,
                        }}
                      />
                    ) : null}
                  </div>
                </div>
              ) : null}
            </div>
          }
        >
          {draft ? (
            <div className={styles.formRow}>
              <div className={styles.splitField}>
                <Input
                  label="Traffic to variant B (%)"
                  type="number"
                  min={minSplit}
                  max={maxSplit}
                  value={split}
                  explanationText={splitValid ? undefined : `Between ${minSplit} and ${maxSplit}: both variants need traffic.`}
                  invalid={!splitValid}
                  onChange={(event) => setSplit(event.target.value)}
                />
              </div>
              <Button
                label="Save split"
                color={ButtonColor.Secondary}
                disabled={!splitValid || working}
                onClick={() => run(() => void setSplitCommand({ splitPercent }))}
              />
              <Button label="Start experiment" color={ButtonColor.Primary} disabled={working} onClick={() => setConfirming('start')} />
            </div>
          ) : null}

          {draft ? (
            <p style={muted}>
              Variant B is a copy of the live tuning. Edit it in the Rules, Synonyms, Field weights and Stopwords tabs above, then start the
              experiment. Nobody sees variant B until then.
            </p>
          ) : null}
        </Card>
      </div>

      {loading ? <Spinner /> : null}

      {failed ? (
        <Callout
          type={CalloutType.FriendlyWarning}
          placement={CalloutPlacementType.OnDesk}
          subheadline="Friendly warning"
          headline="This experiment could not be read"
          actionButton={<Button label="Load again" color={ButtonColor.Secondary} onClick={() => void load()} />}
        >
          <p role="alert">{report.error}</p>
        </Callout>
      ) : null}

      {loaded ? (
        <div aria-live="polite">
          <Stack spacing={Spacing.XL}>
            {draft ? null : (
              <>
                <div className={styles.variants}>
                  <VariantCard stats={report.a} title="Variant A — live tuning" />
                  <VariantCard stats={report.b} title="Variant B — draft tuning" />
                </div>

                <Callout type={CalloutType.QuickTip} placement={CalloutPlacementType.OnDesk} subheadline="Quick tip" headline="What these numbers are">
                  <p>
                    A: {count(report.a.searches)} searches / B: {count(report.b.searches)} searches
                    {report.started === '' ? '' : `, ${report.started} UTC to ${report.ended === '' ? 'now' : `${report.ended} UTC`}`}.
                  </p>
                  <p>
                    These are the rates observed on those searches, nothing more. The page does not test them for statistical significance and does
                    not pick a winner: with small samples the two sides differ by chance most of the time. Let the experiment collect enough traffic
                    that you would bet on the difference yourself before concluding it.
                  </p>
                </Callout>
              </>
            )}
          </Stack>
        </div>
      ) : null}

      {dialog === undefined ? null : (
        <Dialog
          isOpen
          isDismissable
          actionInProgress={working}
          headline={dialog.headline}
          headerCloseButton={{ tooltipText: 'Close' }}
          onClose={() => setConfirming(undefined)}
          cancelAction={{ label: 'Cancel', disabled: working, onClick: () => setConfirming(undefined) }}
          confirmAction={{
            label: dialog.label,
            destructive: dialog.destructive,
            disabled: working,
            inProgress: working,
            onClick: dialog.onConfirm,
          }}
        >
          {dialog.body}
        </Dialog>
      )}
    </Stack>
  );
};
