import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AdminOffer, formatMoney } from '../../../core/admin/admin-pricing-api';
import { AdminPricing } from './admin-pricing';

const OFFERS_URL = '/api/v1/admin/pricing/offers';

function offer(overrides: Partial<AdminOffer> = {}): AdminOffer {
  return {
    id: '11111111-1111-4111-8111-111111111111',
    code: 'membership-monthly',
    name: 'Membership',
    description: 'All access.',
    kind: 'Membership',
    courseId: null,
    courseTitle: null,
    status: 'Draft',
    providerLinked: false,
    commercialFieldsEditable: true,
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    updatedAtUtc: '2026-01-01T00:00:00+00:00',
    prices: [],
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

function setup() {
  TestBed.configureTestingModule({
    imports: [AdminPricing],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
  });

  return {
    fixture: TestBed.createComponent(AdminPricing),
    http: TestBed.inject(HttpTestingController),
  };
}

function host(fixture: { nativeElement: HTMLElement }): HTMLElement {
  return fixture.nativeElement;
}

describe('formatMoney', () => {
  it('places the decimal point from the currency, not from a hard-coded rule', () => {
    // 999 minor units is 9.99 in a two-decimal currency and 999 in a zero-decimal one.
    expect(formatMoney(999, 'USD')).toContain('9.99');
    expect(formatMoney(999, 'JPY')).toContain('999');
  });
});

describe('AdminPricing', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('offers no way back once a price is retired', () => {
    const { fixture, http } = setup();

    http.expectOne(OFFERS_URL).flush([
      offer({
        status: 'Active',
        prices: [
          {
            id: 'aaaaaaaa-1111-4111-8111-111111111111',
            amountMinor: 999,
            currency: 'USD',
            billingInterval: 'Month',
            billingIntervalCount: 1,
            status: 'Retired',
            effectiveFromUtc: '2026-01-01T00:00:00+00:00',
            retiredAtUtc: '2026-03-01T00:00:00+00:00',
            editable: false,
            rowVersion: 'AAAAAAAAAAE=',
          },
        ],
      }),
    ]);
    fixture.detectChanges();

    const dom = host(fixture);
    expect(
      dom.querySelector('[data-testid="price-active-aaaaaaaa-1111-4111-8111-111111111111"]'),
    ).toBeNull();
    expect(
      dom.querySelector('[data-testid="price-retired-aaaaaaaa-1111-4111-8111-111111111111"]'),
    ).toBeNull();
    expect(dom.textContent).toContain('publish a new price to change the amount');
  });

  it('formats the stored amount using the returned currency', () => {
    const { fixture, http } = setup();

    http.expectOne(OFFERS_URL).flush([
      offer({
        prices: [
          {
            id: 'bbbbbbbb-1111-4111-8111-111111111111',
            amountMinor: 4999,
            currency: 'GBP',
            billingInterval: 'OneTime',
            billingIntervalCount: 1,
            status: 'Draft',
            effectiveFromUtc: '2026-01-01T00:00:00+00:00',
            retiredAtUtc: null,
            editable: true,
            rowVersion: 'AAAAAAAAAAE=',
          },
        ],
      }),
    ]);
    fixture.detectChanges();

    // 4999 minor units, formatted from the currency the API returned.
    expect(host(fixture).textContent).toContain('49.99');
  });

  it('sends no provider identifier when creating an offer', () => {
    const { fixture, http } = setup();
    http.expectOne(OFFERS_URL).flush([]);
    fixture.detectChanges();

    const dom = host(fixture);
    const code = dom.querySelector<HTMLInputElement>('[data-testid="offer-code"]')!;
    const name = dom.querySelector<HTMLInputElement>('[data-testid="offer-name"]')!;
    const description = dom.querySelector<HTMLTextAreaElement>('#field-description')!;

    for (const [field, value] of [
      [code, 'membership-monthly'],
      [name, 'Membership'],
      [description, 'All access.'],
    ] as const) {
      field.value = value;
      field.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();

    dom.querySelector<HTMLButtonElement>('[data-testid="create-offer"]')!.click();

    const request = http.expectOne(OFFERS_URL);
    expect(request.request.method).toBe('POST');
    expect(Object.keys(request.request.body)).toEqual([
      'code',
      'name',
      'description',
      'kind',
      'courseId',
    ]);

    request.flush(offer());
    http.expectOne(OFFERS_URL).flush([offer()]);
  });

  it('shows a recoverable error rather than an empty page when the load fails', () => {
    const { fixture, http } = setup();

    http.expectOne(OFFERS_URL).flush('', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(host(fixture).textContent).toContain('could not load pricing');
  });
});
