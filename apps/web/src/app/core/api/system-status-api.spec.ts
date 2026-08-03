import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SystemStatusApi } from './system-status-api';
import { SystemStatus } from './system-status.model';

describe('SystemStatusApi', () => {
  let service: SystemStatusApi;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SystemStatusApi);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GETs the relative /api/v1/system/status URL and maps the response', () => {
    const expected: SystemStatus = {
      status: 'ok',
      service: "Daniel's Dojo API",
      environment: 'Development',
      utcTimestamp: '2026-08-03T00:00:00Z',
    };

    let received: SystemStatus | undefined;
    service.getStatus().subscribe((value) => {
      received = value;
    });

    const request = httpMock.expectOne('/api/v1/system/status');
    expect(request.request.method).toBe('GET');
    request.flush(expected);

    expect(received).toEqual(expected);
  });
});
