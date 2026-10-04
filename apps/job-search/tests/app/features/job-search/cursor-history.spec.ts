import { CursorHistory } from '../../../../src/app/features/job-search/cursor-history';
import { defaultQuery } from '../../../../src/app/features/job-search/search-query';
describe('Bounded cursor navigation', () => {
  it('has no previous page for an unknown deep link and rejects invalid/repeated next cursors', () => {
    const history = new CursorHistory(); history.visit({ ...defaultQuery, cursor: 'deep' });
    expect(history.canPrevious()).toBe(false); expect(history.previous()).toBeUndefined();
    for (const cursor of [null, '', 'bad cursor', 'deep']) expect(history.prepareNext(cursor)).toBe(false);
  });
  it('restores known visits and resets history for criteria changes', () => {
    const history = new CursorHistory(); history.visit(defaultQuery);
    expect(history.prepareNext('next')).toBe(true);
    history.visit({ ...defaultQuery, cursor: 'next' });
    expect(history.previous()).toBeNull(); expect(history.canPrevious()).toBe(true);
    history.visit(defaultQuery); expect(history.canPrevious()).toBe(false);
    expect(history.prepareNext('next')).toBe(true);
    history.visit({ ...defaultQuery, cursor: 'next' });
    history.visit(defaultQuery);
    expect(history.prepareNext('replacement')).toBe(true);
    history.visit({ ...defaultQuery, cursor: 'replacement' });
    expect(history.previous()).toBeNull();
    history.visit({ ...defaultQuery, q: 'new', cursor: 'replacement' });
    expect(history.canPrevious()).toBe(false);
  });
  it('keeps at most 50 visited cursors and treats evicted/unknown pages as deep links', () => {
    const history = new CursorHistory(); history.visit(defaultQuery);
    for (let i = 1; i <= 60; i++) {
      expect(history.prepareNext(String(i))).toBe(true);
      history.visit({ ...defaultQuery, cursor: String(i) });
    }
    expect(history.previous()).toBe('59');
    for (let i = 59; i >= 11; i--) history.visit({ ...defaultQuery, cursor: String(i) });
    expect(history.canPrevious()).toBe(false);
    history.visit({ ...defaultQuery, cursor: 'unknown' }); expect(history.previous()).toBeUndefined();
  });
});
