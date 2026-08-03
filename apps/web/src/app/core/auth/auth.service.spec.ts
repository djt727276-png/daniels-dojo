import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MsalService } from '@azure/msal-angular';

import { AUTH_CONFIG, AuthConfig } from '../configuration/auth-config';
import { AuthService } from './auth.service';

const SESSION_URL = '/api/v1/auth/session';

const configuredAuth: AuthConfig = {
  authority: 'https://danielsdojo.ciamlogin.com/tenant/v2.0',
  clientId: 'spa-client-id',
  redirectUri: 'http://localhost:4200/',
  postLogoutRedirectUri: 'http://localhost:4200/',
  knownAuthorities: ['danielsdojo.ciamlogin.com'],
  apiBaseUrl: '/api',
  apiScope: 'api://api-client-id/access_as_user',
};

class MsalServiceStub {
  loginRedirectCalls: unknown[] = [];
  logoutRedirectCalls: unknown[] = [];
  accounts: unknown[] = [];

  readonly instance = {
    getAllAccounts: () => this.accounts,
  };

  loginRedirect(request: unknown): void {
    this.loginRedirectCalls.push(request);
  }

  logoutRedirect(request: unknown): void {
    this.logoutRedirectCalls.push(request);
  }
}

function setup(config: AuthConfig = configuredAuth) {
  const msal = new MsalServiceStub();

  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: MsalService, useValue: msal },
      { provide: AUTH_CONFIG, useValue: config },
    ],
  });

  return {
    msal,
    service: TestBed.inject(AuthService),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('AuthService', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('starts the redirect sign-in with the configured API scope', () => {
    const { service, msal } = setup();

    service.signIn();

    expect(msal.loginRedirectCalls.length).toBe(1);
    expect(msal.loginRedirectCalls[0]).toEqual({ scopes: [configuredAuth.apiScope] });
  });

  it('does not redirect when authentication is not configured', () => {
    const { service, msal } = setup({
      ...configuredAuth,
      authority: '',
      clientId: '',
      apiScope: '',
    });

    service.signIn();

    expect(msal.loginRedirectCalls.length).toBe(0);
    expect(service.sessionState().kind).toBe('error');
  });

  it('signs out through MSAL with the configured post-logout URI', () => {
    const { service, msal } = setup();

    service.signOut();

    expect(msal.logoutRedirectCalls).toEqual([
      { postLogoutRedirectUri: configuredAuth.postLogoutRedirectUri },
    ]);
    expect(service.sessionState().kind).toBe('signedOut');
  });

  it('reports signed out without calling the API when MSAL holds no account', () => {
    const { service, http } = setup();

    service.refreshSession();

    http.expectNone(SESSION_URL);
    expect(service.sessionState().kind).toBe('signedOut');
  });

  it('uses the API response for identity and roles', () => {
    const { service, msal, http } = setup();
    msal.accounts = [{ homeAccountId: 'account' }];

    service.refreshSession();

    http.expectOne(SESSION_URL).flush({
      userId: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
      displayName: 'Test Customer',
      email: 'customer@example.test',
      roles: ['Student', 'Admin'],
    });

    expect(service.sessionState().kind).toBe('signedIn');
    expect(service.session()?.displayName).toBe('Test Customer');

    // The role comes from the response body, not from anything decoded in the browser.
    expect(service.isAdmin()).toBe(true);
  });

  it('does not treat a Student as an administrator', () => {
    const { service, msal, http } = setup();
    msal.accounts = [{ homeAccountId: 'account' }];

    service.refreshSession();

    http.expectOne(SESSION_URL).flush({
      userId: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
      displayName: 'Student Person',
      email: 'student@example.test',
      roles: ['Student'],
    });

    expect(service.isAdmin()).toBe(false);
  });

  it('maps 401 to signed out', () => {
    const { service, msal, http } = setup();
    msal.accounts = [{ homeAccountId: 'account' }];

    service.refreshSession();
    http.expectOne(SESSION_URL).flush('', { status: 401, statusText: 'Unauthorized' });

    expect(service.sessionState().kind).toBe('signedOut');
  });

  it('maps 403 to a forbidden state', () => {
    const { service, msal, http } = setup();
    msal.accounts = [{ homeAccountId: 'account' }];

    service.refreshSession();
    http.expectOne(SESSION_URL).flush('', { status: 403, statusText: 'Forbidden' });

    expect(service.sessionState().kind).toBe('forbidden');
  });

  it('maps other failures to a recoverable error without retaining the error body', () => {
    const { service, msal, http } = setup();
    msal.accounts = [{ homeAccountId: 'account' }];

    service.refreshSession();
    http
      .expectOne(SESSION_URL)
      .flush(
        { accessToken: 'super-secret-token', code: 'authorization-code' },
        { status: 500, statusText: 'Server Error' },
      );

    const state = service.sessionState();
    expect(state.kind).toBe('error');

    // No token, code, or claim material is carried in the state the UI renders.
    expect(JSON.stringify(state)).not.toContain('super-secret-token');
    expect(JSON.stringify(state)).not.toContain('authorization-code');
  });
});
