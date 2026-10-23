import { describeChange } from './dashboard-page.component';

describe('describeChange', () => {
  it('shows a percentage against the previous period', () => {
    expect(describeChange(15, 10, '24h')).toBe('+50% vs the 24h before');
    expect(describeChange(5, 10, '24h')).toBe('-50% vs the 24h before');
  });

  it('says so when nothing changed', () => {
    expect(describeChange(10, 10, '7 days')).toBe('no change from the 7 days before');
  });

  it('does not divide by zero', () => {
    expect(describeChange(0, 0, '24h')).toBe('no reports in the 24h before');
    expect(describeChange(4, 0, '24h')).toContain('none in the 24h before');
  });
});
