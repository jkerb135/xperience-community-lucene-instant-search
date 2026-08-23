import { Card, Colors, Headline, HeadlineSize, Inline, Spacing } from '@kentico/xperience-admin-components';

import { muted } from '../theme';

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

const Legend = ({ color, label }: { readonly color: string; readonly label: string }) => (
  <span style={{ ...muted, display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
    <span aria-hidden="true" style={{ width: '16px', height: '2px', background: color, display: 'inline-block' }} />
    {label}
  </span>
);

export const VolumeChart = ({ points, formatDay }: VolumeChartProps) => {
  const peak = Math.max(...points.map((point) => point.volume), 1);
  const labels = points.filter((_, index) => index % Math.ceil(points.length / 6) === 0);

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
        style={{ display: 'block', width: '100%', height: '220px' }}
      >
        {[0, 60, 120].map((y) => (
          <line key={y} x1="0" y1={y} x2={width} y2={y} stroke={Colors.DividerDefault} strokeWidth="1" strokeDasharray="3 4" />
        ))}
        <line x1="0" y1={height} x2={width} y2={height} stroke={Colors.BorderDefault} strokeWidth="1" />
        <path d={path(points.map((point) => point.volume), peak)} fill="none" stroke={searchesColor} strokeWidth="2" />
        <path d={path(points.map((point) => point.zeroResultVolume), peak)} fill="none" stroke={zeroColor} strokeWidth="2" />
      </svg>
      <div style={{ display: 'flex', justifyContent: 'space-between' }}>
        {labels.map((point) => (
          <span key={point.day} style={muted}>
            {formatDay(point.day)}
          </span>
        ))}
      </div>
      <details>
        <summary>Show the numbers</summary>
        <table>
          <caption style={muted}>Searches per day</caption>
          <thead>
            <tr>
              <th scope="col">Day</th>
              <th scope="col">Searches</th>
              <th scope="col">Zero-result</th>
            </tr>
          </thead>
          <tbody>
            {points.map((point) => (
              <tr key={point.day}>
                <td>{formatDay(point.day)}</td>
                <td>{point.volume}</td>
                <td>{point.zeroResultVolume}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </Card>
  );
};
