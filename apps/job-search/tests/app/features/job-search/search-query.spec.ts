import { convertToParamMap } from '@angular/router';
import { criteriaKey, defaultQuery, parseQuery, queryKey, queryParams } from '../../../../src/app/features/job-search/search-query';
describe('URL query contract', () => {
  it('defaults absent parameters and ignores unknown values', () => {
    expect(parseQuery(convertToParamMap({ unknown: 'value' }))).toEqual({ query: defaultQuery, errors: [] });
    expect(queryParams(defaultQuery)).toEqual({});
  });
  it('round trips encoded opaque text and all allowlisted settings', () => {
    const query = { ...defaultQuery, q: 'C++ & /?', department: 'Platform', location: 'Montréal', sort: 'closing-soon' as const, limit: 50, cursor: 'opaque+/=?' };
    expect(parseQuery(convertToParamMap(queryParams(query))).query).toEqual(query);
    expect(criteriaKey(query)).toBe(queryKey({ ...query, cursor: null }));
    expect(Object.isFrozen(parseQuery(convertToParamMap({})).query)).toBe(true);
  });
  it('trims without changing case and accepts exact limits', () => {
    expect(parseQuery(convertToParamMap({ q: '  Case  ', department: 'A'.repeat(100), location: 'B'.repeat(100), limit: '01', sort: 'newest' })).query).toEqual({ ...defaultQuery, q: 'Case', department: 'A'.repeat(100), location: 'B'.repeat(100), limit: 1 });
    expect(parseQuery(convertToParamMap({ q: 'a'.repeat(200) })).errors).toEqual([]);
  });
  it.each([{ q: 'a'.repeat(201) }, { department: 'a'.repeat(101) }, { location: 'a'.repeat(101) }, { q: ['one', 'two'] }, { sort: ['newest', 'newest'] }, { sort: '' }, { sort: 'unknown' }, { limit: ['20', '20'] }, { limit: '' }, { limit: '1.5' }, { limit: '0' }, { limit: '51' }, { limit: 'Infinity' }, { cursor: '' }, { cursor: 'bad cursor' }, { cursor: ['one', 'two'] }])('repairs invalid URL state %s', params => {
    const result = parseQuery(convertToParamMap(params));
    expect(result.errors.length).toBeGreaterThan(0);
    expect(result.query.cursor).toBeNull();
  });
  it('drops a valid cursor if its criteria needed repair', () => expect(parseQuery(convertToParamMap({ sort: 'bad', cursor: 'valid' })).query.cursor).toBeNull());
});
