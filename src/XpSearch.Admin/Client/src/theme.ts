import type { CSSProperties } from 'react';
import { Colors } from '@kentico/xperience-admin-components';

/*
 * The text treatments the design asks for that no component exposes, built from the design system's
 * own colour tokens, so there is no stylesheet and nothing to keep in sync with a theme. See
 * docs/adr/0020-admin-page-design.md.
 */

/** Secondary prose: hints under a figure, notes next to a headline. */
export const muted: CSSProperties = { color: Colors.TextLowEmphasis, fontSize: '12px', lineHeight: '16px', margin: 0 };

/** An inline group: a headline and the chip beside it, 8px apart. */
export const flexRow: CSSProperties = { display:"flex", flexDirection: 'row', alignItems: 'center', justifyContent: 'start', columnGap: '8px' };

/** The one figure a KPI tile or a stats strip is about. */
export const figure: CSSProperties = { fontSize: '32px', lineHeight: '38px', fontWeight: 700, margin: 0 };

/** A figure that reports state rather than a KPI: the status page's document / source counts. */
export const stateFigure: CSSProperties = { fontSize: '24px', lineHeight: '32px', fontWeight: 700, margin: 0 };
