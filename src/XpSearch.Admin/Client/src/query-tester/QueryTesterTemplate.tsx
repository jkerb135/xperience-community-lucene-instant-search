import { useState } from 'react';
import {
  Button,
  ButtonColor,
  ButtonSize,
  ButtonType,
  Callout,
  CalloutPlacementType,
  CalloutType,
  Card,
  Colors,
  Cols,
  Column,
  Headline,
  HeadlineSize,
  Icon,
  Inline,
  Input,
  LayoutAlignment,
  MenuItem,
  NameToggleButtons,
  Row,
  Select,
  Spacing,
  Spinner,
  Stack,
  Tag,
  useMediaBreakpoints,
} from '@kentico/xperience-admin-components';
import type { IconName } from '@kentico/xperience-admin-components';
import { usePageCommand } from '@kentico/xperience-admin-base';

import { mono, muted } from '../theme';

/*
 * Client template of the query tester (spec 8.4), built to the owner's design spec:
 * https://claude.ai/design/p/d9cffec1-046f-46e2-b611-d162418351f9 (artboards 2a-2d). Registered as
 * "@xperience-community/xperience-search/QueryTester"; the back end is
 * XpSearch.Admin.UIPages.QueryTester.QueryTesterPage. See docs/adr/0020-admin-page-design.md.
 */

interface ContactGroup {
  readonly codeName: string;
  readonly displayName: string;
}

interface QueryTesterProps {
  /** The index under test. It comes from the URL, so it is shown and never chosen. */
  readonly selectedIndexName: string;
  /** The content languages the index is configured for. Empty when the index indexes every language. */
  readonly languages: string[];
  /** The contact groups a run can be simulated as, so a group-scoped rule can be seen firing. */
  readonly contactGroups: ContactGroup[];
}

type ResultChange = 'Unchanged' | 'MovedUp' | 'MovedDown' | 'Injected' | 'Removed';

interface Hit {
  readonly id: string;
  readonly title: string;
  readonly url: string;
  readonly score: number;
  readonly position: number;
  readonly baseScore: number;
  readonly boosts: string[];
  readonly change: ResultChange;
}

interface Side {
  readonly hits: Hit[];
  readonly total: number;
  readonly tookMs: number;
  readonly queryExplanations: string[];
}

interface RunResult {
  readonly withRules: Side;
  readonly withoutRules: Side;
  readonly error: string;
}

interface RunData {
  readonly query: string;
  readonly language: string;
  readonly pageSize: number;
  /** Code name of the contact group to simulate; empty runs as the signed-in admin's own contact. */
  readonly contactGroup?: string;
}

const Commands = {
  Run: 'Run',
  OpenStatus: 'OpenStatus',
};

const pageSizes = [10, 25, 50];
const anyLanguage = '';
const realVisitor = '';

const changes: Record<ResultChange, { readonly label: string; readonly icon: IconName; readonly color: Colors }> = {
  Unchanged: { label: 'Unchanged', icon: 'xp-minus', color: Colors.BackgroundTagGrey },
  MovedUp: { label: 'Moved up by a rule', icon: 'xp-arrow-up', color: Colors.BackgroundTagSkyBlue },
  MovedDown: { label: 'Moved down by a rule', icon: 'xp-arrow-down', color: Colors.BackgroundTagYellow },
  Injected: { label: 'Added by a rule', icon: 'xp-plus', color: Colors.BackgroundTagNeonGreen },
  Removed: { label: 'Removed by a rule', icon: 'xp-ban-sign', color: Colors.BackgroundTagRose },
};

const round = (value: number): string => value.toFixed(3);

/** "English (en)" where the browser knows the code, the bare code where it does not. */
const languageLabel = (code: string): string => {
  const name = new Intl.DisplayNames(undefined, { type: 'language', fallback: 'code' }).of(code);

  return name === undefined || name === code ? code : `${name} (${code})`;
};

const changed = (side: Side): number => side.hits.filter((hit) => hit.change !== 'Unchanged').length;

/** The change marker: an icon and a label, so it never depends on the tag colour alone. */
const ChangeTag = ({ change }: { readonly change: ResultChange }) => (
  <Inline spacing={Spacing.XS}>
    <Icon name={changes[change].icon} />
    <Tag label={changes[change].label} readOnly background={{ color: changes[change].color }} />
  </Inline>
);

const HitRow = ({ hit }: { readonly hit: Hit }) => (
  <Card>
    <Row spacing={Spacing.M}>
      <Column width={3}>
        <strong>{hit.position}</strong>
      </Column>
      <Column cols={Cols.Col8}>
        <Stack spacing={Spacing.XS}>
          <strong>{hit.title || hit.id}</strong>
          {hit.url === '' ? null : <p style={mono}>{hit.url}</p>}
          <ChangeTag change={hit.change} />
          {hit.boosts.map((boost) => (
            <p key={boost} style={muted}>
              {boost}
            </p>
          ))}
        </Stack>
      </Column>
      <Column cols={Cols.Col2}>
        <Stack align={LayoutAlignment.End}>
          <strong>{round(hit.score)}</strong>
          <p style={muted}>{round(hit.baseScore)}</p>
        </Stack>
      </Column>
    </Row>
  </Card>
);

const Stats = ({ side }: { readonly side: Side }) => (
  <div aria-live="polite">
    <Inline spacing={Spacing.L}>
      <span>
        <strong>{side.total}</strong> results
      </span>
      <span>
        <strong>{side.tookMs}</strong> ms
      </span>
      <span>
        <strong>{changed(side)}</strong> changed
      </span>
    </Inline>
  </div>
);

const Hits = ({ side }: { readonly side: Side }) => (
  <Stack spacing={Spacing.M}>
    <Stats side={side} />
    {side.hits.length === 0 ? (
      <p style={muted}>No results.</p>
    ) : (
      side.hits.map((hit) => <HitRow key={hit.id} hit={hit} />)
    )}
  </Stack>
);

const SideCard = ({ side, title, note }: { readonly side: Side; readonly title: string; readonly note: string }) => (
  <Card
    fullHeight
    headline={<Headline size={HeadlineSize.S}>{title}</Headline>}
    description={<Tag label={note} readOnly background={{ color: Colors.BackgroundTagGrey }} />}
  >
    <Hits side={side} />
  </Card>
);

const Placeholder = ({ label }: { readonly label: string }) => (
  <Card fullHeight>
    <p style={muted}>{label}</p>
  </Card>
);

const Explanations = ({ lines, open }: { readonly lines: string[]; readonly open: boolean }) => (
  <Card
    headline={<Headline size={HeadlineSize.S}>Rewritten query per pipeline stage</Headline>}
    description={<p style={muted}>{`${lines.length} stage${lines.length === 1 ? '' : 's'}`}</p>}
  >
    <details open={open}>
      <summary>Show the stages</summary>
      <Stack spacing={Spacing.S}>
        {lines.map((line, index) => (
          <Row key={line} spacing={Spacing.L}>
            <Column width={4}>
              <p style={mono}>{`${index + 1} ·`}</p>
            </Column>
            <Column cols={Cols.Col9}>
              <p style={mono}>{line}</p>
            </Column>
          </Row>
        ))}
      </Stack>
    </details>
  </Card>
);

export const QueryTesterTemplate = ({ selectedIndexName, languages, contactGroups }: QueryTesterProps) => {
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState(anyLanguage);
  const [pageSize, setPageSize] = useState(pageSizes[0]);
  const [contactGroup, setContactGroup] = useState(realVisitor);
  const [ran, setRan] = useState('');
  const [result, setResult] = useState<RunResult | undefined>(undefined);
  const [running, setRunning] = useState(false);
  const [side, setSide] = useState('withRules');
  const { sm: narrow } = useMediaBreakpoints();

  const { execute: run } = usePageCommand<RunResult, RunData>(Commands.Run, {
    after: (response) => {
      setRunning(false);
      setResult(response);
    },
  });

  const { execute: openStatus } = usePageCommand<void, void>(Commands.OpenStatus);

  const submit = (nextLanguage: string) => {
    setLanguage(nextLanguage);
    setRan(query);
    setRunning(true);
    void run({ query, language: nextLanguage, pageSize, contactGroup });
  };

  const empty = query.trim() === '';
  const failed = !running && result !== undefined && result.error !== '';
  const loaded = !running && result !== undefined && result.error === '';
  const fallback = languages.find((code) => code !== language);

  const subtitle = loaded
    ? narrow
      ? ` · “${ran}”`
      : ' · results with the index’s tuning applied, next to the same query without it'
    : '';

  return (
    <Stack spacing={Spacing.XL}>
      <div>
        <Headline size={HeadlineSize.L}>Query tester</Headline>
        <p style={muted}>
          Index <strong>{selectedIndexName}</strong>
          {subtitle}
        </p>
      </div>

      <Card>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            submit(language);
          }}
        >
          <Row spacing={Spacing.L} alignY={LayoutAlignment.End}>
            <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
              <Input
                label="Query"
                markAsRequired
                placeholder="e.g. espresso"
                explanationText={empty ? 'Enter a query to compare results. Required.' : undefined}
                value={query}
                onChange={(event) => setQuery(event.target.value)}
              />
            </Column>
            <Column>
              <Select label="Language" value={language} onChange={(value) => setLanguage(value ?? anyLanguage)}>
                <MenuItem primaryLabel="Any language" value={anyLanguage} />
                {languages.map((code) => (
                  <MenuItem key={code} primaryLabel={languageLabel(code)} value={code} />
                ))}
              </Select>
            </Column>
            <Column>
              <Select label="Page size" value={String(pageSize)} onChange={(value) => setPageSize(Number(value) || pageSizes[0])}>
                {pageSizes.map((size) => (
                  <MenuItem key={size} primaryLabel={String(size)} value={String(size)} />
                ))}
              </Select>
            </Column>
            <Column>
              <Select
                label="Contact group"
                value={contactGroup}
                onChange={(value) => setContactGroup(value ?? realVisitor)}
              >
                <MenuItem primaryLabel="Real visitor (your contact)" value={realVisitor} />
                {contactGroups.map((group) => (
                  <MenuItem key={group.codeName} primaryLabel={group.displayName} value={group.codeName} />
                ))}
              </Select>
            </Column>
            <Column>
              <Button
                label="Run"
                type={ButtonType.Submit}
                color={ButtonColor.Primary}
                size={ButtonSize.M}
                inProgress={running}
                disabled={empty}
              />
            </Column>
          </Row>
        </form>
      </Card>

      {running ? <Spinner /> : null}

      {failed ? (
        <Callout
          type={CalloutType.FriendlyWarning}
          placement={CalloutPlacementType.OnDesk}
          subheadline="Friendly warning"
          headline="The query could not be run"
          actionButton={
            <Inline spacing={Spacing.M}>
              <Button
                label="Open status"
                color={ButtonColor.Secondary}
                onClick={() => {
                  void openStatus();
                }}
              />
              {fallback === undefined ? null : (
                <Button label={`Try ${languageLabel(fallback)}`} color={ButtonColor.Tertiary} onClick={() => submit(fallback)} />
              )}
            </Inline>
          }
        >
          <p role="alert">{result.error}</p>
        </Callout>
      ) : null}

      {!running && result === undefined ? (
        <>
          <Callout
            type={CalloutType.QuickTip}
            placement={CalloutPlacementType.OnDesk}
            subheadline="Quick tip"
            headline="What this page shows"
          >
            Running a query gives you two result lists: one with the tuning of this index applied, one against the raw
            index. Positions, scores and the rules that moved a document are marked on every row, so you can tell whether
            a rule did what you meant.
          </Callout>
          <Row spacing={Spacing.L}>
            <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
              <Placeholder label="With tuning — results appear here" />
            </Column>
            <Column cols={narrow ? Cols.Col12 : Cols.Col6}>
              <Placeholder label="Without tuning — results appear here" />
            </Column>
          </Row>
        </>
      ) : null}

      {loaded && narrow ? (
        <Card>
          <Stack spacing={Spacing.M}>
            <NameToggleButtons
              selectedItemId={side}
              items={[
                { id: 'withRules', label: `With tuning (${result.withRules.total})` },
                { id: 'withoutRules', label: `Without tuning (${result.withoutRules.total})` },
              ]}
              onChange={setSide}
            />
            <Hits side={side === 'withRules' ? result.withRules : result.withoutRules} />
          </Stack>
        </Card>
      ) : null}

      {loaded && !narrow ? (
        <Row spacing={Spacing.L}>
          <Column cols={Cols.Col6}>
            <SideCard side={result.withRules} title="With tuning" note="Rules, synonyms, stopwords, field weights" />
          </Column>
          <Column cols={Cols.Col6}>
            <SideCard side={result.withoutRules} title="Without tuning" note="Raw index, no rules applied" />
          </Column>
        </Row>
      ) : null}

      {loaded && result.withRules.queryExplanations.length > 0 ? (
        <Explanations lines={result.withRules.queryExplanations} open={!narrow} />
      ) : null}
    </Stack>
  );
};
