import { TestBed } from '@angular/core/testing';
import { UrlTree, provideRouter } from '@angular/router';
import { Signal, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

import { Session, SessionState } from './session.model';
import { adminGuard, authenticatedGuard } from './admin.guard';
import { AuthService } from './auth.service';

/**
 * Minimal stand-in exposing only what the guards read: the session state signal.
 *
 * The guards are user experience: they keep a member from landing on a screen that would
 * fail anyway. Authorization itself happens on the server, which re-checks the local
 * database role on every request — so these tests assert routing behaviour, not security.
 */
class FakeAuth {
  readonly state = signal<SessionState>({ kind: 'loading' });

  readonly sessionState: Signal<SessionState> = this.state;
}

function session(roles: readonly string[]): Session {
  return {
    userId: '11111111-1111-4111-8111-111111111111',
    displayName: 'Test Member',
    email: 'member@example.test',
    roles: [...roles],
  };
}

function setup() {
  const auth = new FakeAuth();

  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
  });

  return { auth };
}

async function run(guard: typeof adminGuard): Promise<boolean | UrlTree> {
  const result = TestBed.runInInjectionContext(() =>
    guard(null as never, null as never),
  ) as Observable<boolean | UrlTree>;

  return firstValueFrom(result);
}

describe('authenticatedGuard', () => {
  it('sends a signed-out visitor to the page that offers sign-in', async () => {
    const { auth } = setup();
    auth.state.set({ kind: 'signedOut' });

    const result = await run(authenticatedGuard);

    expect(result).toBeInstanceOf(UrlTree);
    expect(String(result)).toBe('/account');
  });

  it('lets a signed-in member through', async () => {
    const { auth } = setup();
    auth.state.set({ kind: 'signedIn', session: session(['Student']) });

    expect(await run(authenticatedGuard)).toBe(true);
  });

  it('waits for a loading session instead of bouncing a refresh', async () => {
    const { auth } = setup();

    // The guard must not decide while the state is 'loading' — a hard refresh on a
    // protected route would otherwise always bounce. It answers once the state settles.
    const pending = run(authenticatedGuard);
    auth.state.set({ kind: 'signedIn', session: session(['Student']) });

    expect(await pending).toBe(true);
  });
});

describe('adminGuard', () => {
  it('turns a Student away from an admin route reached by typing the URL', async () => {
    const { auth } = setup();
    auth.state.set({ kind: 'signedIn', session: session(['Student']) });

    const result = await run(adminGuard);

    expect(result).toBeInstanceOf(UrlTree);
    expect(String(result)).toBe('/account');
  });

  it('lets a member through when the API reported the Admin role', async () => {
    const { auth } = setup();
    auth.state.set({ kind: 'signedIn', session: session(['Admin', 'Student']) });

    expect(await run(adminGuard)).toBe(true);
  });

  it('refuses while signed out entirely', async () => {
    const { auth } = setup();
    auth.state.set({ kind: 'signedOut' });

    expect(await run(adminGuard)).toBeInstanceOf(UrlTree);
  });
});
