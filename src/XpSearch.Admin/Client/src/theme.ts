import type { CSSProperties } from 'react';
import { Colors } from '@kentico/xperience-admin-components';

/*
 * The two text treatments the design asks for that no component exposes: low-emphasis prose and the
 * monospace panels of the query tester. Both are built from the design system's own colour tokens,
 * so there is no stylesheet and nothing to keep in sync with a theme. See
 * docs/adr/0020-admin-page-design.md.
 */

/** Secondary prose: hints under a figure, notes next to a headline. */
export const muted: CSSProperties = { color: Colors.TextLowEmphasis, fontSize: '12px', lineHeight: '16px', margin: 0 };

/** A rewritten query or a URL, where character alignment carries meaning. */
export const mono: CSSProperties = { fontFamily: 'monospace', fontSize: '12px', lineHeight: '18px', margin: 0 };

/** The one figure a KPI tile or a stats strip is about. */
export const figure: CSSProperties = { fontSize: '32px', lineHeight: '38px', fontWeight: 700, margin: 0 };
