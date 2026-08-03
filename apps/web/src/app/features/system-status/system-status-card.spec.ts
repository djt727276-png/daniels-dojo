import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SystemStatus } from '../../core/api/system-status.model';
import { SystemStatusCard } from './system-status-card';

const STATUS_URL = '/api/v1/system/status';

const HEALTHY: SystemStatus = {
  status: 'ok',
  service: "Daniel's Dojo API",
  environment: 'Development',
  utcTimestamp: '2026-08-03T00:00:00Z',
};

describe('SystemStatusCard', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SystemStatusCard],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows the loading state while the request is in flight', () => {
    const fixture = TestBed.createComponent(SystemStatusCard);
    fixture.detectChanges();

    httpMock.expectOne(STATUS_URL);
    expect(text(fixture)).toContain('Checking API');
  });

  it('shows the healthy state after a successful response', () => {
    const fixture = TestBed.createComponent(SystemStatusCard);
    fixture.detectChanges();

    httpMock.expectOne(STATUS_URL).flush(HEALTHY);
    fixture.detectChanges();

    const rendered = text(fixture);
    expect(rendered).toContain('API is healthy');
    expect(rendered).toContain("Daniel's Dojo API");
    expect(rendered).toContain('Development');
  });

  it('shows the unavailable state with a working retry after an error', () => {
    const fixture = TestBed.createComponent(SystemStatusCard);
    fixture.detectChanges();

    httpMock.expectOne(STATUS_URL).error(new ProgressEvent('network error'));
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('API is unavailable');

    const retry = element.querySelector('button');
    expect(retry).toBeTruthy();
    retry?.click();
    fixture.detectChanges();

    httpMock.expectOne(STATUS_URL).flush(HEALTHY);
    fixture.detectChanges();
    expect(text(fixture)).toContain('API is healthy');
  });
});

function text(fixture: { nativeElement: HTMLElement }): string {
  return fixture.nativeElement.textContent ?? '';
}
