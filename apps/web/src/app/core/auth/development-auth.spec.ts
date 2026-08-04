import {
  AuthConfig,
  isAuthConfigured,
  isDevelopmentAuthAllowed,
} from '../configuration/auth-config';
import { environment } from '../../../environments/environment';

const base: AuthConfig = {
  mode: 'development',
  authority: '',
  clientId: '',
  redirectUri: 'http://localhost:4200/',
  postLogoutRedirectUri: 'http://localhost:4200/',
  knownAuthorities: [],
  apiBaseUrl: '/api',
  apiScope: '',
};

describe('authentication mode selection', () => {
  it('allows the Development harness only in a non-production bundle', () => {
    // The test bundle is a development build, so the harness is permitted here.
    expect(environment.production).toBe(false);
    expect(isDevelopmentAuthAllowed(base)).toBe(true);
  });

  it('never allows the Development harness in entra mode', () => {
    expect(isDevelopmentAuthAllowed({ ...base, mode: 'entra' })).toBe(false);
  });

  it('treats the Development harness as a working configuration', () => {
    // Development mode needs no tenant values to be considered configured.
    expect(isAuthConfigured(base)).toBe(true);
  });

  it('does not treat empty entra configuration as configured', () => {
    expect(isAuthConfigured({ ...base, mode: 'entra' })).toBe(false);
  });

  it('treats fully populated entra configuration as configured', () => {
    expect(
      isAuthConfigured({
        ...base,
        mode: 'entra',
        authority: 'https://tenant.ciamlogin.com/id/v2.0',
        clientId: 'spa-client',
        apiScope: 'api://api-client/access_as_user',
      }),
    ).toBe(true);
  });

  it('ships the production environment pinned to entra', async () => {
    // Guards the rule that a production build can never select the harness. The check is on
    // the shipped file rather than a mock, so editing it would fail this test.
    const production = await import('../../../environments/environment.production');

    expect(production.environment.production).toBe(true);
    expect(production.environment.auth.mode).toBe('entra');
    expect(isDevelopmentAuthAllowed(production.environment.auth)).toBe(false);
  });
});
