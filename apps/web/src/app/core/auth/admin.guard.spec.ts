import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { Signal, signal } from '@angular/core';

import { Session } from './session.model';
import { adminGuard, authenticatedGuard } from './admin.guard';
import { AuthService } from './auth.service';

/**
 * Minimal stand-in exposing only what the guards read.
 *
 * The guards are user experience: they keep a member from landing on a screen that would fail
 * anyway. Authorization itself happens on the server, which re-checks the local database role
 * on every request — so these tests assert routing behaviour, not security.
 */
class FakeAuth {
  readonly current = signal<Session | null>(null);

  readonly session: Signal<Session | null> = this.current;

  readonly isAdmin: Signal<boolean> = signal(false) as unknown as Signal<boolean>;

  private readonly adminFlag = signal(false);

  constructor() {
    (this as { isAdmin: Signal<boolean> }).isAdmin = this.adminFlag;
  }

  signInAs(session: Session | null, admin: boolean): void {
    this.current.set(session);
    this.adminFlag.set(admin);
  }
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

  return { auth, router: TestBed.inject(Router) };
}

function run(guard: typeof adminGuard): boolean | UrlTree {
  return TestBed.runInInjectionContext(() => guard(null as never, null as never)) as
    boolean | UrlTree;
}

describe('authenticatedGuard', () => {
  it('sends a signed-out visitor to the page that offers sign-in', () => {
    const { auth } = setup();
    auth.signInAs(null, false);

    const result = run(authenticatedGuard);

    expect(result).toBeInstanceOf(UrlTree);
    expect(String(result)).toBe('/account');
  });

  it('lets a signed-in member through', () => {
    const { auth } = setup();
    auth.signInAs(session(['Student']), false);

    expect(run(authenticatedGuard)).toBe(true);
  });
});

describe('adminGuard', () => {
  it('turns a Student away from an admin route reached by typing the URL', () => {
    const { auth } = setup();
    auth.signInAs(session(['Student']), false);

    const result = run(adminGuard);

    expect(result).toBeInstanceOf(UrlTree);
    expect(String(result)).toBe('/account');
  });

  it('lets a member through when the API reported the Admin role', () => {
    const { auth } = setup();
    auth.signInAs(session(['Admin', 'Student']), true);

    expect(run(adminGuard)).toBe(true);
  });

  it('reads the role from the session the API returned, not from a token', () => {
    const { auth } = setup();

    // A session that merely claims Admin in its role list is still gated on what the
    // AuthService computed from the API response — and the API is the real boundary.
    auth.signInAs(session(['Admin']), false);

    expect(run(adminGuard)).toBeInstanceOf(UrlTree);
  });
});
