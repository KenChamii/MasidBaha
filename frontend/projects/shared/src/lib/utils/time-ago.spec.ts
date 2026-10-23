import { timeAgo } from './time-ago';

describe('timeAgo', () => {
  const ago = (ms: number) => new Date(Date.now() - ms).toISOString();

  it.each([
    [10_000, 'ngayon lang'],
    [5 * 60_000, '5m ago'],
    [59 * 60_000, '59m ago'],
    [3 * 3_600_000, '3h ago'],
    [2 * 86_400_000, '2d ago']
  ])('%d ms ago reads as "%s"', (ms, expected) => {
    expect(timeAgo(ago(ms))).toBe(expected);
  });
});
