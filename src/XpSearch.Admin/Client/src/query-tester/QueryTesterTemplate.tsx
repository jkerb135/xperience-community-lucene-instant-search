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

/*
 * Client template of the query tester (spec 8.4). Registered as
 * "@yourco/xperience-search-admin/QueryTester"; the back end is XpSearch.Admin.UIPages.QueryTester.QueryTesterPage.
 * https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages
 */

interface QueryTesterProps {
  readonly indexNames: string[];
  readonly selectedIndexName: string;
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
  readonly indexName: string;
  readonly query: string;
  readonly language: string;
  readonly pageSize: number;
}

const Commands = {
  Run: 'Run',
};

const changeLabels: Record<ResultChange, string> = {
  Unchanged: '',
  MovedUp: 'Moved up by a rule',
  MovedDown: 'Moved down by a rule',
  Injected: 'Added by a rule',
  Removed: 'Removed by a rule',
};

const changeMarks: Record<ResultChange, string> = {
  Unchanged: '',
  MovedUp: '▲',
  MovedDown: '▼',
  Injected: '+',
  Removed: '−',
};

const round = (value: number): string => value.toFixed(3);

const ChangeMark = ({ change }: { readonly change: ResultChange }) => {
  if (change === 'Unchanged') {
    return null;
  }

  return (
    <span
      style={{
        marginLeft: '8px',
        padding: '0 6px',
        borderRadius: '4px',
        border: '1px solid currentColor',
        fontSize: '12px',
      }}
    >
      <span aria-hidden="true">{changeMarks[change]} </span>
      {changeLabels[change]}
    </span>
  );
};

const HitRow = ({ hit }: { readonly hit: Hit }) => (
  <li style={{ padding: '8px 0', borderBottom: '1px solid rgba(0,0,0,.1)' }}>
    <div>
      <strong>
        {hit.position}. {hit.title || hit.id}
      </strong>
      <ChangeMark change={hit.change} />
    </div>
    {hit.url ? <div style={{ fontSize: '12px' }}>{hit.url}</div> : null}
    <div style={{ fontSize: '12px' }}>
      score {round(hit.score)} (base score {round(hit.baseScore)})
    </div>
    {hit.boosts.length > 0 ? (
      <ul style={{ fontSize: '12px', margin: '4px 0 0 16px' }}>
        {hit.boosts.map((boost) => (
          <li key={boost}>{boost}</li>
        ))}
      </ul>
    ) : null}
  </li>
);

const SideColumn = ({ side, title, description }: { readonly side: Side; readonly title: string; readonly description: string }) => (
  <section style={{ flex: '1 1 0', minWidth: '320px' }} aria-label={title}>
    <Headline size={HeadlineSize.S}>{title}</Headline>
    <p style={{ fontSize: '12px' }}>{description}</p>
    <p style={{ fontSize: '12px' }}>
      {side.total} result(s), {side.tookMs} ms
    </p>
    {side.queryExplanations.length > 0 ? (
      <>
        <Headline size={HeadlineSize.S}>How the query was rewritten</Headline>
        <ul style={{ fontSize: '12px', margin: '0 0 8px 16px' }}>
          {side.queryExplanations.map((line) => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      </>
    ) : null}
    {side.hits.length === 0 ? (
      <p>No results.</p>
    ) : (
      <ol style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {side.hits.map((hit) => (
          <HitRow key={hit.id} hit={hit} />
        ))}
      </ol>
    )}
  </section>
);

export const QueryTesterTemplate = ({ indexNames, selectedIndexName }: QueryTesterProps) => {
  const [indexName, setIndexName] = useState(selectedIndexName);
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState('');
  const [result, setResult] = useState<RunResult | undefined>(undefined);
  const [running, setRunning] = useState(false);

  const { execute: run } = usePageCommand<RunResult, RunData>(Commands.Run, {
    after: (response) => {
      setRunning(false);
      setResult(response);
    },
  });

  const submit = () => {
    setRunning(true);
    void run({ indexName, query, language, pageSize: 10 });
  };

  return (
    <div style={{ padding: '16px' }}>
      <Headline size={HeadlineSize.L}>Query tester</Headline>
      <p>
        Runs the query twice: once the way a visitor sees it, and once with none of this index&apos;s rules, synonyms,
        stopwords or field weights.
      </p>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
        style={{ display: 'flex', flexWrap: 'wrap', gap: '16px', alignItems: 'flex-end' }}
      >
        <div style={{ minWidth: '240px' }}>
          <Select label="Index" value={indexName} onChange={(value) => setIndexName(value ?? '')}>
            {indexNames.map((name) => (
              <MenuItem key={name} primaryLabel={name} value={name} />
            ))}
          </Select>
        </div>
        <div style={{ minWidth: '240px' }}>
          <Input label="Query" value={query} onChange={(event) => setQuery(event.target.value)} />
        </div>
        <div style={{ minWidth: '160px' }}>
          <Input
            label="Language"
            explanationText="Optional, for example en."
            value={language}
            onChange={(event) => setLanguage(event.target.value)}
          />
        </div>
        <Button
          label="Run"
          type={ButtonType.Submit}
          color={ButtonColor.Primary}
          size={ButtonSize.M}
          inProgress={running}
          disabled={indexName === ''}
        />
      </form>

      <div aria-live="polite" style={{ marginTop: '24px' }}>
        {running ? <Spinner /> : null}
        {!running && result && result.error !== '' ? <p role="alert">{result.error}</p> : null}
        {!running && result && result.error === '' ? (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '32px' }}>
            <SideColumn
              side={result.withRules}
              title="With rules"
              description="What a visitor searching this index gets right now."
            />
            <SideColumn
              side={result.withoutRules}
              title="Without rules"
              description="The same query with no relevance tuning applied at all."
            />
          </div>
        ) : null}
      </div>
    </div>
  );
};
