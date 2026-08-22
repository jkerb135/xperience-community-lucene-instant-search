import type { ReactNode } from 'react';
import { Headline, HeadlineSize } from '@kentico/xperience-admin-components';

/*
 * A plain semantic table. The design system's Table component is built for listing pages - it is
 * virtualized and driven by column/cell descriptors - which buys nothing for six small reports and
 * costs the row-level action markup. See docs/adr/0016-admin-client.md.
 */

export interface Column<TRow> {
  readonly key: string;
  readonly caption: string;
  readonly numeric?: boolean;
  readonly render: (row: TRow) => ReactNode;
}

interface ReportTableProps<TRow> {
  readonly title: string;
  readonly description?: string;
  readonly columns: Array<Column<TRow>>;
  readonly rows: TRow[];
  readonly rowKey: (row: TRow) => string;
  readonly emptyText?: string;
}

export const ReportTable = <TRow,>({
  title,
  description,
  columns,
  rows,
  rowKey,
  emptyText = 'No searches in this range.',
}: ReportTableProps<TRow>) => (
  <section style={{ flex: '1 1 420px', minWidth: '320px' }}>
    <Headline size={HeadlineSize.S}>{title}</Headline>
    {description ? <p style={{ fontSize: '12px' }}>{description}</p> : null}
    {rows.length === 0 ? (
      <p>{emptyText}</p>
    ) : (
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <caption style={{ position: 'absolute', width: '1px', height: '1px', overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
          {title}
        </caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                style={{ textAlign: column.numeric ? 'right' : 'left', borderBottom: '1px solid rgba(0,0,0,.2)', padding: '4px 8px' }}
              >
                {column.caption}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((column) => (
                <td
                  key={column.key}
                  style={{ textAlign: column.numeric ? 'right' : 'left', borderBottom: '1px solid rgba(0,0,0,.1)', padding: '4px 8px' }}
                >
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    )}
  </section>
);
