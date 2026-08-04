import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';
import { Session, SessionState } from '../../core/auth/session.model';
import { Account } from './account';

class AuthServiceStub {
  readonly state = signal<SessionState>({ kind: 'loading' });
  readonly configured = signal(true);

  signInCalls = 0;
  createAccountCalls = 0;
  signOutCalls = 0;
  refreshCalls = 0;

  readonly sessionState = this.state.asReadonly();

  session = () => {
    const current = this.state();
    return current.kind === 'signedIn' ? current.session : null;
  };

  isAdmin = () => this.session()?.roles.includes('Admin') ?? false;
  isConfigured = () => this.configured();

  signIn(): void {
    this.signInCalls += 1;
  }

  createAccount(): void {
    this.createAccountCalls += 1;
  }

  signOut(): void {
    this.signOutCalls += 1;
  }

  refreshSession(): void {
    this.refreshCalls += 1;
  }
}

const student: Session = {
  userId: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
  displayName: 'Student Person',
  email: 'student@example.test',
  roles: ['Student'],
};

function setup() {
  const auth = new AuthServiceStub();

  TestBed.configureTestingModule({
    imports: [Account],
    providers: [{ provide: AuthService, useValue: auth }],
  });

  const fixture = TestBed.createComponent(Account);
  return { auth, fixture };
}

function text(fixture: { nativeElement: HTMLElement }): string {
  return fixture.nativeElement.textContent ?? '';
}

describe('Account', () => {
  it('shows the loading state while the session is being checked', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Checking your session');
  });

  it('offers separate create-account and sign-in actions when signed out', () => {
    const { auth, fixture } = setup();
    auth.state.set({ kind: 'signedOut' });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    const buttons = Array.from(element.querySelectorAll('button'));
    expect(buttons[0]?.textContent).toContain('Create account');
    expect(buttons[1]?.textContent).toContain('Sign in');

    buttons[0]?.click();
    expect(auth.createAccountCalls).toBe(1);
    expect(auth.signInCalls).toBe(0);

    buttons[1]?.click();
    expect(auth.signInCalls).toBe(1);
  });

  it('renders identity and roles from the API-backed session', () => {
    const { auth, fixture } = setup();
    auth.state.set({ kind: 'signedIn', session: student });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('[data-testid="account-name"]')?.textContent).toContain(
      'Student Person',
    );
    expect(element.querySelector('[data-testid="account-email"]')?.textContent).toContain(
      'student@example.test',
    );
    expect(element.querySelector('[data-testid="account-roles"]')?.textContent).toContain(
      'Student',
    );
  });

  it('hides the administrator notice from a Student', () => {
    const { auth, fixture } = setup();
    auth.state.set({ kind: 'signedIn', session: student });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[data-testid="admin-available"]')).toBeNull();
  });

  it('shows the administrator notice when the API reported Admin', () => {
    const { auth, fixture } = setup();
    auth.state.set({
      kind: 'signedIn',
      session: { ...student, roles: ['Student', 'Admin'] },
    });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[data-testid="admin-available"]')).not.toBeNull();
  });

  it('signs out from the signed-in state', () => {
    const { auth, fixture } = setup();
    auth.state.set({ kind: 'signedIn', session: student });
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    const button = element.querySelector('button');
    button?.click();

    expect(auth.signOutCalls).toBe(1);
  });

  it('explains the forbidden state without exposing anything sensitive', () => {
    const { auth, fixture } = setup();
    auth.state.set({ kind: 'forbidden' });
    fixture.detectChanges();

    const rendered = text(fixture);
    expect(rendered).toContain('cannot access');
    expect(rendered).not.toContain('token');
    expect(rendered).not.toContain('claim');
  });

  it('offers a retry on a recoverable error and exposes no error detail', () => {
    const { auth, fixture } = setup();
    auth.state.set({ kind: 'error' });
    fixture.detectChanges();

    const rendered = text(fixture);
    expect(rendered).toContain('could not load your account');
    expect(rendered).not.toContain('token');
    expect(rendered).not.toContain('Bearer');

    const element: HTMLElement = fixture.nativeElement;
    element.querySelector('button')?.click();
    expect(auth.refreshCalls).toBe(1);
  });

  it('reports when sign-in is not configured instead of offering it', () => {
    const { auth, fixture } = setup();
    auth.configured.set(false);
    fixture.detectChanges();

    expect(text(fixture)).toContain('not configured');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });
});
