// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { escapeHtml, formatNumber, highlight, html, render, toHtml } from './html';
import type { Result } from '../types';

const EVIL = '<script>alert("x")</script>';

describe('html', () => {
  it('escapes interpolated text', () => {
    expect(toHtml(html`<p>${EVIL}</p>`)).toBe(
      '<p>&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;</p>'
    );
  });

  it('escapes quotes so an interpolated attribute value cannot break out', () => {
    const value = '" onclick="alert(1)';
    expect(toHtml(html`<a href="${value}">x</a>`)).toBe(
      '<a href="&quot; onclick=&quot;alert(1)">x</a>'
    );
  });

  it('escapes a single-quoted attribute value too', () => {
    expect(toHtml(html`<a title='${"' onload='x"}'>y</a>`)).toContain('&#39;');
  });

  it('keeps the static parts of the template as authored', () => {
    expect(toHtml(html`<span aria-hidden="true">&times;</span>`)).toBe(
      '<span aria-hidden="true">&times;</span>'
    );
  });

  it('nests results and arrays of results without re-escaping them', () => {
    const rows = ['a & b', 'c'].map((text) => html`<li>${text}</li>`);
    expect(toHtml(html`<ul>${rows}</ul>`)).toBe('<ul><li>a &amp; b</li><li>c</li></ul>');
  });

  it('renders null, undefined and false as nothing, but zero as "0"', () => {
    expect(toHtml(html`${null}${undefined}${false}${0}`)).toBe('0');
  });

  it('only trusts raw markup when it is opted into', () => {
    expect(toHtml(html`${html.raw('<b>ok</b>')}`)).toBe('<b>ok</b>');
    expect(toHtml(html`${'<b>no</b>'}`)).toBe('&lt;b&gt;no&lt;/b&gt;');
  });

  it('does not trust a payload that merely looks like a template result', () => {
    const forged = JSON.parse('{"value":"<img onerror=alert(1)>"}') as unknown;
    expect(toHtml(html`${forged as never}`)).not.toContain('<img');
  });

  it('renders into a container with one innerHTML write', () => {
    const container = document.createElement('div');
    render(html`<p>${'a<b'}</p>`, container);
    expect(container.innerHTML).toBe('<p>a&lt;b</p>');
    expect(container.querySelector('p')?.textContent).toBe('a<b');
  });

  it('escapes a plain string returned by a template', () => {
    const container = document.createElement('div');
    render('<b>x</b>', container);
    expect(container.innerHTML).toBe('&lt;b&gt;x&lt;/b&gt;');
  });
});

describe('escapeHtml', () => {
  it('covers the five HTML metacharacters', () => {
    expect(escapeHtml(`&<>"'`)).toBe('&amp;&lt;&gt;&quot;&#39;');
  });
});

describe('highlight', () => {
  const asResult = (extra: Partial<Result>): Result =>
    ({ id: 'doc-1', attributes: {}, ...extra }) as Result;

  it('returns the server highlight and adds the shell class to each mark', () => {
    const marked = highlight(
      'title',
      asResult({ highlights: { title: '<mark>Es</mark>presso &amp; <mark>Milk</mark>' } })
    );
    expect(marked.value).toBe(
      '<mark class="xps-highlight">Es</mark>presso &amp; <mark class="xps-highlight">Milk</mark>'
    );
  });

  it('falls back to the escaped plain attribute', () => {
    expect(highlight('title', asResult({ attributes: { title: EVIL } })).value).toBe(
      '&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;'
    );
  });

  it('is empty when neither the highlight nor the field exists', () => {
    expect(highlight('nope', asResult({})).value).toBe('');
  });
});

describe('formatNumber', () => {
  it('groups with Intl', () => {
    expect(formatNumber(1234567, 'en-US')).toBe('1,234,567');
    expect(formatNumber(1234567, 'de-DE')).toBe('1.234.567');
  });
});
