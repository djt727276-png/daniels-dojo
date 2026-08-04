import { AuthConfig } from '../configuration/auth-config';
import { targetsConfiguredApi } from './api-token.interceptor';

const config: AuthConfig = {
  mode: 'entra',
  authority: 'https://danielsdojo.ciamlogin.com/tenant/v2.0',
  clientId: 'spa-client-id',
  redirectUri: 'http://localhost:4200/',
  postLogoutRedirectUri: 'http://localhost:4200/',
  knownAuthorities: ['danielsdojo.ciamlogin.com'],
  apiBaseUrl: 'https://api.danielsdojo.test/api',
  apiScope: 'api://api-client-id/access_as_user',
};

describe('apiTokenInterceptor target matching', () => {
  it('attaches to the exact configured API path', () => {
    expect(targetsConfiguredApi('https://api.danielsdojo.test/api', config)).toBe(true);
    expect(targetsConfiguredApi('https://api.danielsdojo.test/api/v1/auth/session', config)).toBe(
      true,
    );
  });

  it('never attaches to a third-party origin', () => {
    expect(targetsConfiguredApi('https://evil.test/api/v1/auth/session', config)).toBe(false);
    expect(targetsConfiguredApi('https://graph.microsoft.com/v1.0/me', config)).toBe(false);
  });

  it('never attaches to a lookalike host', () => {
    // Suffix-style lookalikes are the classic way a naive prefix check leaks a token.
    expect(targetsConfiguredApi('https://api.danielsdojo.test.evil.test/api/v1', config)).toBe(
      false,
    );
    expect(targetsConfiguredApi('https://api-danielsdojo.test/api/v1', config)).toBe(false);
    expect(targetsConfiguredApi('https://xapi.danielsdojo.test/api/v1', config)).toBe(false);
  });

  it('never attaches when the scheme or port differs', () => {
    expect(targetsConfiguredApi('http://api.danielsdojo.test/api/v1', config)).toBe(false);
    expect(targetsConfiguredApi('https://api.danielsdojo.test:8443/api/v1', config)).toBe(false);
  });

  it('never attaches to a path that merely shares a prefix', () => {
    expect(targetsConfiguredApi('https://api.danielsdojo.test/apifoo', config)).toBe(false);
    expect(targetsConfiguredApi('https://api.danielsdojo.test/apidocs/v1', config)).toBe(false);
  });

  it('never attaches to unrelated paths on the same origin', () => {
    expect(targetsConfiguredApi('https://api.danielsdojo.test/assets/logo.svg', config)).toBe(
      false,
    );
  });

  it('rejects malformed URLs rather than guessing', () => {
    expect(targetsConfiguredApi('http://[::bad', config)).toBe(false);
  });

  it('matches relative URLs against the document origin', () => {
    const relativeConfig: AuthConfig = { ...config, apiBaseUrl: '/api' };

    expect(targetsConfiguredApi('/api/v1/auth/session', relativeConfig)).toBe(true);
    expect(targetsConfiguredApi('/other/v1', relativeConfig)).toBe(false);
    expect(targetsConfiguredApi('https://evil.test/api/v1', relativeConfig)).toBe(false);
  });
});
