import { formatLevel, formatPrice } from './catalog.model';

describe('price formatting', () => {
  it('renders minor units using the currency the API returned', () => {
    expect(formatPrice({ amountMinor: 1999, currency: 'USD', interval: 'OneTime' })).toContain(
      '19.99',
    );
  });

  it('adds the cadence for a recurring price', () => {
    const formatted = formatPrice({ amountMinor: 999, currency: 'USD', interval: 'Month' });

    expect(formatted).toContain('9.99');
    expect(formatted).toContain('/month');
  });

  it('does not add a cadence to a one-time price', () => {
    expect(formatPrice({ amountMinor: 1999, currency: 'USD', interval: 'OneTime' })).not.toContain(
      '/month',
    );
  });

  it('honours a non-USD currency from the response', () => {
    // Nothing about the amount or currency is decided in the client.
    const formatted = formatPrice({ amountMinor: 2500, currency: 'EUR', interval: 'OneTime' });

    expect(formatted).toContain('25');
    expect(formatted).not.toContain('$');
  });

  it('renders nothing when there is no price', () => {
    expect(formatPrice(null)).toBe('');
  });

  it('never hard-codes a launch amount', () => {
    // A different stored amount must produce a different display.
    expect(formatPrice({ amountMinor: 4200, currency: 'USD', interval: 'OneTime' })).toContain(
      '42.00',
    );
  });
});

describe('level formatting', () => {
  it('expands the AllLevels value for display', () => {
    expect(formatLevel('AllLevels')).toBe('All levels');
  });

  it('passes other levels through', () => {
    expect(formatLevel('Beginner')).toBe('Beginner');
  });
});
