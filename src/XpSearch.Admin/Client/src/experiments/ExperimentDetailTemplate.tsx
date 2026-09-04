import { useState } from 'react';
import {
  Button,
  ButtonColor,
  Callout,
  CalloutPlacementType,
  CalloutType,
  Card,
  Colors,
  Cols,
  Column,
  Dialog,
  Headline,
  HeadlineSize,
  Input,
  LayoutAlignment,
  Row,
  Spacing,
  Spinner,
  Stack,
  Tag,
  useMediaBreakpoints,
} from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { figure, muted } from '../theme';

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
  <div>
    <p style={muted}>{label}</p>
    <p style={figure}>{value}</p>
    <p style={muted}>{hint}</p>
  </div>
);

const VariantCard = ({ stats, title, description }: { readonly stats: VariantStats; readonly title: string; readonly description: string }) => (
  <Card headline={title} description={description} fullHeight>
    <Stack spacing={Spacing.L}>
      <Figure label="Searches" value={count(stats.searches)} hint="sample size" />
      <div>
        <Row spacing={Spacing.L}>
          <Column>
            <Figure
              label="Zero-result rate"
              value={rate(stats.zeroResultSearches, stats.searches)}
              hint={`${count(stats.zeroResultSearches)} found nothing`}
            />
          </Column>
          <Column>
            <Figure label="Click-through rate" value={rate(stats.clicks, stats.searches)} hint={`${count(stats.clicks)} clicks`} />
          </Column>
          <Column>
            <Figure label="Avg. clicked position" value={position(stats.averageClickedPosition)} hint="lower is better" />
          </Column>
        </Row>
      </div>
    </Stack>
  </Card>
);

export const ExperimentDetailTemplate = ({ indexName, minSplit, maxSplit }: ExperimentDetailProps) => {
  const [report, setReport] = useState<Report | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [split, setSplit] = useState('');
  const [confirming, setConfirming] = useState<Confirmation>(undefined);
  const [working, setWorking] = useState(false);
  const { sm: narrow } = useMediaBreakpoints();

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
      <div>
        <Headline size={HeadlineSize.L}>{loaded ? report.name : 'Experiment'}</Headline>
        <p style={muted}>
          Index <strong>{indexName}</strong>
          {loaded ? ` · ${report.splitPercent}% of traffic to variant B` : ''}
          {loaded && report.started !== '' ? ` · started ${report.started} UTC` : ''}
          {loaded && report.ended !== '' ? ` · ended ${report.ended} UTC` : ''}
        </p>
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
            <Card>
              <Row spacing={Spacing.L} alignY={LayoutAlignment.Center}>
                <Column>
                  <Tag
                    label={report.state}
                    readOnly
                    background={{ color: running ? Colors.SuccessBackgroundHighEmphasis : Colors.BackgroundTagGrey }}
                  />
                  {concluded ? (
                    <Tag
                      label={`Variant B ${report.outcome.toLowerCase()}`}
                      readOnly
                      background={{ color: report.outcome === 'Promoted' ? Colors.SuccessBackgroundHighEmphasis : Colors.BackgroundTagGrey }}
                    />
                  ) : null}
                </Column>
                <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
                  {draft ? (
                    <Row spacing={Spacing.M} alignY={LayoutAlignment.End}>
                      <Column>
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
                      </Column>
                      <Column>
                        <Button
                          label="Save split"
                          color={ButtonColor.Secondary}
                          disabled={!splitValid || working}
                          onClick={() => run(() => void setSplitCommand({ splitPercent }))}
                        />
                      </Column>
                      <Column>
                        <Button label="Start experiment" color={ButtonColor.Primary} disabled={working} onClick={() => setConfirming('start')} />
                      </Column>
                    </Row>
                  ) : null}

                  {running ? (
                    <Row spacing={Spacing.M} alignY={LayoutAlignment.End}>
                      <Column>
                        <Button label="Promote B to live" color={ButtonColor.Primary} destructive disabled={working} onClick={() => setConfirming('promote')} />
                      </Column>
                      <Column>
                        <Button label="Discard B" color={ButtonColor.Secondary} destructive disabled={working} onClick={() => setConfirming('discard')} />
                      </Column>
                    </Row>
                  ) : null}
                </Column>
              </Row>

              {draft ? (
                <p style={muted}>
                  Variant B is a copy of the live tuning. Edit it in the Rules, Synonyms, Field weights and Stopwords tabs above, then start the
                  experiment. Nobody sees variant B until then.
                </p>
              ) : null}
            </Card>

            {draft ? null : (
              <>
                {/*
                  * Row carries a negative margin-top of its own spacing (it compensates the gutter
                  * padding its Columns add), which would cancel the Stack's gap and leave the
                  * variant cards touching the status card. The plain div takes the 24px instead.
                  */}
                <div>
                  <Row spacing={Spacing.L}>
                    <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
                      <VariantCard stats={report.a} title="Variant A — live tuning" description={`${count(report.a.searches)} searches`} />
                    </Column>
                    <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
                      <VariantCard stats={report.b} title="Variant B — draft tuning" description={`${count(report.b.searches)} searches`} />
                    </Column>
                  </Row>
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
