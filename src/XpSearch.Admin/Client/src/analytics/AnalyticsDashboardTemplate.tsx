import {Fragment, useState} from 'react';
import {
    Button,
    ButtonColor,
    ButtonSize,
    ButtonType,
    Callout,
    CalloutPlacementType,
    CalloutType,
    Card,
    Cols,
    Column,
    ColumnContentType,
    DateTimeRangeInput,
    FormItemWrapper,
    Headline,
    HeadlineSize,
    MenuItem,
    NameToggleButtons,
    Row,
    Select,
    Spacing,
    Spinner,
    Stack,
} from '@kentico/xperience-admin-components';
import type {TableRow} from '@kentico/xperience-admin-components';
import {usePageCommand} from '@kentico/xperience-admin-base';

import {column, node, numberNode, ReportTable, text} from './ReportTable';
import {VolumeChart, VolumePoint} from './VolumeChart';
import {figure, muted} from '../theme';

import styles from './AnalyticsDashboard.module.scss';

/*
 * Client template of the analytics dashboard (spec 9.3), built to the owner's design spec:
 * https://claude.ai/design/p/d9cffec1-046f-46e2-b611-d162418351f9 (artboards 1a-1d). Registered as
 * "@xperience-community/xperience-search/AnalyticsDashboard"; the back end is
 * XpSearch.Admin.UIPages.Analytics.AnalyticsDashboardPage. See docs/adr/0020-admin-page-design.md.
 */

interface AnalyticsDashboardProps {
    /** The index the reports cover. It comes from the URL, so it is shown and never chosen. */
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
    readonly zeroResultSearches: number;
    readonly clicks: number;
    readonly error: string;
}

interface LoadData {
    readonly from: string;
    readonly to: string;
}

interface CreateRuleData {
    readonly query: string;
}

const Commands = {
    Load: 'Load',
    CreateRule: 'CreateRule',
};

const presets = [7, 30, 90];
const rowCounts = [10, 25, 50, 100];
const defaultRange = 30;
const defaultPageSize = 10;

const dayFormat = new Intl.DateTimeFormat(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC'
});
const dayMonthFormat = new Intl.DateTimeFormat(undefined, {day: 'numeric', month: 'short', timeZone: 'UTC'});

/** Parses a yyyy-mm-dd day as UTC midnight, which is the instant the DateTimeInput edits. */
const toDate = (day: string): Date => new Date(`${day}T00:00:00Z`);

const toDay = (date: Date): string => date.toISOString().slice(0, 10);

const shiftDays = (day: string, days: number): string => {
    const date = toDate(day);
    date.setUTCDate(date.getUTCDate() - days);

    return toDay(date);
};

const daysBetween = (from: string, to: string): number =>
    Math.round((toDate(to).getTime() - toDate(from).getTime()) / 86_400_000) + 1;

const count = (value: number): string => value.toLocaleString();

const percent = (value: number): string => `${(value * 100).toFixed(1)}%`;

const position = (value: number | null): string => (value === null ? '—' : value.toFixed(1));

/** A query row: the query bold on the left, every other column a right-aligned number. */
const queryRow = (identifier: string, query: string, numbers: Array<[string, string]>): TableRow => ({
    identifier,
    disabled: false,
    cells: [
        node('query', () => <span className={styles.query} title={query}>{query}</span>),
        ...numbers.map(([name, value]) => numberNode(name, value)),
    ],
});

const Kpi = ({label, value, hint}: { readonly label: string; readonly value: string; readonly hint: string }) => (
    <div className={styles.tile}>
        <Card fullHeight>
            <div className={styles.tileBody}>
                <p style={muted}>{label}</p>
                <p style={figure}>{value}</p>
                <p style={muted}>{hint}</p>
            </div>
        </Card>
    </div>
);

export const AnalyticsDashboardTemplate = ({selectedIndexName, today}: AnalyticsDashboardProps) => {
    const [from, setFrom] = useState(shiftDays(today, defaultRange - 1));
    const [to, setTo] = useState(today);
    const [pageSize, setPageSize] = useState(defaultPageSize);
    const [report, setReport] = useState<Report | undefined>(undefined);
    const [loading, setLoading] = useState(true);
    // Keys the report cards, so a fresh load or a new page size sends every table back to page one.
    const [tableGeneration, setTableGeneration] = useState(0);

    const {execute: load} = usePageCommand<Report, LoadData>(
        Commands.Load,
        {
            data: {from, to},
            executeOnMount: true,
            after: (response) => {
                setLoading(false);
                setReport(response);
                setTableGeneration((generation) => generation + 1);
            },
        },
        [],
    );

    const {execute: createRule} = usePageCommand<void, CreateRuleData>(Commands.CreateRule);

    const reload = (nextFrom: string, nextTo: string) => {
        setFrom(nextFrom);
        setTo(nextTo);
        setLoading(true);
        void load({from: nextFrom, to: nextTo});
    };

    const range = daysBetween(from, to);
    const preset = presets.find((days) => days === range);
    const failed = report !== undefined && report.error !== '';
    const loaded = !loading && report !== undefined && report.error === '';
    const empty = loaded && report.totalSearches === 0;
    const rangeText = `${dayMonthFormat.format(toDate(from))} – ${dayFormat.format(toDate(to))}`;

    /*
     * The page opens with this card (the board has no bare headline above it): the title and the
     * index meta line in the card's headline slot, the filters as one bottom-aligned row under it.
     */
    const controls = (
        <div className={styles.card}>
            <Card
                headline={
                    <div className={styles.cardHeader}>
                        <span>Analytics</span>
                        <p style={muted}>
                            Index <strong>{selectedIndexName}</strong> · Lucene
                            {loaded ? ` · ${rangeText} · ${report.totalSearches === 0 ? 'no searches' : `${count(report.totalSearches)} searches`}` : ''}
                        </p>
                    </div>
                }
            >
                <form
                    className={styles.formRow}
                    onSubmit={(event) => {
                        event.preventDefault();
                        reload(from, to);
                    }}
                >
                    <FormItemWrapper label="Range">
                        <NameToggleButtons
                            selectedItemId={preset === undefined ? '' : String(preset)}
                            items={presets.map((days) => ({id: String(days), label: `${days} days`}))}
                            onChange={(id) => reload(shiftDays(today, Number(id) - 1), today)}
                        />
                    </FormItemWrapper>
                    <div className={styles.dateField}>
                        <FormItemWrapper label="Date range">
                            <DateTimeRangeInput
                                timeZone="UTC"
                                showTime={false}
                                allowClear={false}
                                value={{from: toDate(from), to: toDate(to)}}
                                maxDate={toDate(today)}
                                onChange={(range) => {
                                    if (range === null) {
                                        return;
                                    }
                                    setFrom(toDay(range.from));
                                    setTo(toDay(range.to));
                                }}
                            />
                        </FormItemWrapper>
                    </div>
                    <div className={styles.rowsField}>
                        <Select label="Rows per page" value={String(pageSize)}
                                onChange={(value) => {
                                    setPageSize(Number(value) || defaultPageSize);
                                    setTableGeneration((generation) => generation + 1);
                                }}>
                            {rowCounts.map((rows) => (
                                <MenuItem key={rows} primaryLabel={String(rows)} value={String(rows)}/>
                            ))}
                        </Select>
                    </div>
                    <Button label="Load" type={ButtonType.Submit} color={ButtonColor.Primary} size={ButtonSize.M}
                            inProgress={loading}/>
                </form>
            </Card>
        </div>
    );

    const kpis = loaded
        ? [
            {
                label: 'Total searches',
                value: count(report.totalSearches),
                hint: `${range} days · ${rangeText}`,
            },
            {
                label: 'Zero-result rate',
                value: report.totalSearches === 0 ? '—' : percent(report.zeroResultSearches / report.totalSearches),
                hint:
                    report.totalSearches === 0
                        ? 'No searches to divide by'
                        : `${count(report.zeroResultSearches)} searches returned nothing`,
            },
            {
                label: 'Click-through rate',
                value: report.totalSearches === 0 ? '—' : percent(report.clicks / report.totalSearches),
                hint:
                    report.totalSearches === 0
                        ? 'No searches to divide by'
                        : `${count(report.clicks)} clicks on ${count(report.totalSearches)} searches`,
            },
            {
                label: 'Avg clicked position',
                value: position(report.averageClickedPosition),
                hint: report.averageClickedPosition === null ? 'No clicks recorded' : 'Across all clicked results',
            },
        ]
        : [];

    /*
     * A component cell rather than the stock ActionCell (HW-10 defect 5): ActionCell renders its
     * actions as icon-only Buttons, and Button sets aria-label from its `label` prop, falling back to
     * the literal string "button" - which is how the action announced. TableAction has no aria hook of
     * its own, so the button is rendered here with the row's query in its label, which is both the
     * visible text and the accessible name.
     */
    const zeroResultRows: TableRow[] = loaded
        ? report.zeroResultQueries.map((row) => ({
            identifier: row.query,
            disabled: false,
            cells: [
                node('query', () => <span className={styles.query} title={row.query}>{row.query}</span>),
                numberNode('volume', count(row.volume)),
                text('lastSeen', dayFormat.format(toDate(row.lastSeen))),
                node('action', () => (
                    <Button
                        label="Create rule"
                        icon="xp-plus"
                        color={ButtonColor.Secondary}
                        size={ButtonSize.S}
                        onClick={() => {
                            void createRule({query: row.query});
                        }}
                    />
                )),
            ],
        }))
        : [];

    const zeroResultCard = (
        <ReportTable
            headline="Zero-result queries"
            count={
                loaded
                    ? `${count(report.zeroResultQueries.reduce((sum, row) => sum + row.volume, 0))} searches · ${report.zeroResultQueries.length} queries`
                    : undefined
            }
            note="Only actionable table on this page"
            columns={[
                column('query', 'Query', {minWidth: 35, maxWidth: 35, contentType: ColumnContentType.Component}),
                column('volume', 'Volume', {minWidth: 12, maxWidth: 12, contentType: ColumnContentType.Component}),
                column('lastSeen', 'Last seen', {minWidth: 23, maxWidth: 23}),
                column('action', 'Action', {minWidth: 24, maxWidth: 24, contentType: ColumnContentType.Component}),
            ]}
            rows={zeroResultRows}
            pageSize={pageSize}
            emptyText="Every search in this range found something."
            hint="Create rule opens the Rules form seeded with the query."
            tableClassName={styles.zeroTable}
        />
    );

    /*
     * Column widths are pinned (min = max), as on the query tester: a cell is `min-width:
     * <units>x8px` plus 16px of padding either side inside a grid of `auto` tracks, so a column
     * whose max is larger than its min grows with its content and pushes the row past the card.
     * The board's proportions over the 882px a full-width card holds at 1366px: 3 : 1 : 2 : 2 for
     * the zero-result table, 3 : 1 : 1 here.
     */
    const volumeColumns = [
        column('query', 'Query', {minWidth: 60, maxWidth: 60, contentType: ColumnContentType.Component}),
        column('volume', 'Volume', {minWidth: 19, maxWidth: 19, contentType: ColumnContentType.Component}),
        column('p95', 'p95 time', {minWidth: 19, maxWidth: 19, contentType: ColumnContentType.Component}),
    ];

    const topQueriesCard = (
        <ReportTable
            headline="Top queries"
            pageSize={pageSize}
            columns={volumeColumns}
            rows={
                loaded
                    ? report.topQueries.map((row) =>
                        queryRow(row.query, row.query, [
                            ['volume', count(row.volume)],
                            ['p95', `${row.p95ProcessingTimeMs} ms`],
                        ]),
                    )
                    : []
            }
            emptyText="No searches in this range."
            tableClassName={styles.queryTable}
        />
    );

    const clickThroughCard = (
        <ReportTable
            headline="Click-through"
            pageSize={pageSize}
            columns={[
                column('query', 'Query', {minWidth: 38, maxWidth: 38, contentType: ColumnContentType.Component}),
                column('volume', 'Vol.', {minWidth: 13, maxWidth: 13, contentType: ColumnContentType.Component}),
                column('clicks', 'Clicks', {minWidth: 13, maxWidth: 13, contentType: ColumnContentType.Component}),
                column('ctr', 'CTR', {minWidth: 13, maxWidth: 13, contentType: ColumnContentType.Component}),
                column('pos', 'Avg pos.', {minWidth: 13, maxWidth: 13, contentType: ColumnContentType.Component}),
            ]}
            rows={
                loaded
                    ? report.clickThrough.map((row) =>
                        queryRow(row.query, row.query, [
                            ['volume', count(row.volume)],
                            ['clicks', count(row.clicks)],
                            ['ctr', percent(row.clickThroughRate)],
                            ['pos', row.averageClickedPosition === null ? 'No data' : position(row.averageClickedPosition)],
                        ]),
                    )
                    : []
            }
            emptyText="Nothing was clicked in this range."
            tableClassName={styles.clickTable}
            footer={
                <Row spacing={Spacing.S}>
                    <Column cols={Cols.Col9}>
                        <p style={muted}>Average clicked position, all queries</p>
                    </Column>
                    <Column cols={Cols.Col3}>
                        <strong>{loaded ? position(report.averageClickedPosition) : '—'}</strong>
                    </Column>
                </Row>
            }
        />
    );

    const slowestCard = (
        <ReportTable
            headline="Slowest queries"
            pageSize={pageSize}
            columns={volumeColumns}
            rows={
                loaded
                    ? report.slowestQueries.map((row) =>
                        queryRow(row.query, row.query, [
                            ['volume', count(row.volume)],
                            ['p95', `${row.p95ProcessingTimeMs} ms`],
                        ]),
                    )
                    : []
            }
            emptyText="No searches in this range."
            tableClassName={styles.queryTable}
        />
    );

    return (
        <Stack spacing={Spacing.XL}>
            {controls}

            {failed ? (
                <Callout
                    type={CalloutType.FriendlyWarning}
                    placement={CalloutPlacementType.OnDesk}
                    subheadline="Friendly warning"
                    headline="Analytics could not be loaded"
                    actionButton={<Button label="Load again" color={ButtonColor.Secondary}
                                          onClick={() => reload(from, to)}/>}
                >
                    <p role="alert">{report.error}</p>
                </Callout>
            ) : null}

            {loading ? <Spinner/> : null}

            <div aria-live="polite">
                <Stack spacing={Spacing.XL}>
                    {loaded ? (
                        <div className={styles.tiles}>
                            {kpis.map((kpi) => (
                                <Kpi key={kpi.label} {...kpi} />
                            ))}
                        </div>
                    ) : null}

                    {empty ? (
                        <Card>
                            <Headline size={HeadlineSize.S}>No searches in this range</Headline>
                            <p style={muted}>
                                Nothing was searched on {selectedIndexName} between {rangeText}. Widen the range or
                                check that search
                                logging is enabled for the index.
                            </p>
                            <Button
                                label={`Load last ${defaultRange} days`}
                                color={ButtonColor.Tertiary}
                                onClick={() => reload(shiftDays(today, defaultRange - 1), today)}
                            />
                        </Card>
                    ) : null}

                    {loaded && !empty ? (
                        <Fragment key={tableGeneration}>
                            <VolumeChart points={report.volumeOverTime}
                                         pageSize={pageSize}
                                         formatDay={(day) => dayMonthFormat.format(toDate(day))}/>
                            {zeroResultCard}
                            {topQueriesCard}
                            {clickThroughCard}
                            {slowestCard}
                        </Fragment>
                    ) : null}
                </Stack>
            </div>
        </Stack>
    );
};
