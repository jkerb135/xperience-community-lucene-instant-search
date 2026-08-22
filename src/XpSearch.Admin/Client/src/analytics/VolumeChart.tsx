import { Headline, HeadlineSize } from '@kentico/xperience-admin-components';

/*
 * Search volume over time (spec 9.3). The design system exposes no bar or line chart - only
 * FunnelChart - and a chart library is not worth a dependency for one series, so this is plain SVG
 * with a table fallback for screen readers. See docs/adr/0016-admin-client.md.
 */

export interface VolumePoint {
  readonly day: string;
  readonly volume: number;
}

interface VolumeChartProps {
  readonly points: VolumePoint[];
}

const height = 120;

export const VolumeChart = ({ points }: VolumeChartProps) => {
  if (points.length === 0) {
    return (
      <section>
        <Headline size={HeadlineSize.S}>Search volume over time</Headline>
        <p>No searches in this range.</p>
      </section>
    );
  }

  const peak = Math.max(...points.map((point) => point.volume), 1);
  const barWidth = 100 / points.length;

  return (
    <section style={{ flex: '1 1 100%' }}>
      <Headline size={HeadlineSize.S}>Search volume over time</Headline>
      <svg
        viewBox={`0 0 100 ${height}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`Search volume per day, peaking at ${peak} searches. The same numbers are in the table below.`}
        style={{ width: '100%', height: `${height}px` }}
      >
        {points.map((point, index) => (
          <rect
            key={point.day}
            x={index * barWidth}
            y={height - (point.volume / peak) * height}
            width={Math.max(barWidth - 0.4, 0.2)}
            height={(point.volume / peak) * height}
            fill="currentColor"
          >
            <title>{`${point.day}: ${point.volume}`}</title>
          </rect>
        ))}
      </svg>
      <details>
        <summary>Show the numbers</summary>
        <table style={{ borderCollapse: 'collapse' }}>
          <caption>Searches per day</caption>
          <thead>
            <tr>
              <th scope="col" style={{ textAlign: 'left', padding: '2px 8px' }}>
                Day
              </th>
              <th scope="col" style={{ textAlign: 'right', padding: '2px 8px' }}>
                Searches
              </th>
            </tr>
          </thead>
          <tbody>
            {points.map((point) => (
              <tr key={point.day}>
                <td style={{ padding: '2px 8px' }}>{point.day}</td>
                <td style={{ textAlign: 'right', padding: '2px 8px' }}>{point.volume}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </section>
  );
};
