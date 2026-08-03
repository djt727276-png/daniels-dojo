import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';

import { AuthService } from '../../core/auth/auth.service';
import { Session } from '../../core/auth/session.model';
import { AppShell } from './app-shell';

/**
 * Stands in for the real service so the shell tests stay free of MSAL and HTTP. Admin
 * visibility is driven by the roles the API reported, exactly as in the real service.
 */
class AuthServiceStub {
  readonly current = signal<Session | null>(null);

  session = () => this.current();
  isAdmin = () => this.current()?.roles.includes('Admin') ?? false;
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
    imports: [AppShell],
    providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
  });

  return { auth, fixture: TestBed.createComponent(AppShell) };
}

describe('AppShell', () => {
  it('creates the shell', () => {
    const { fixture } = setup();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders header and main landmarks with the product title', () => {
    const { fixture } = setup();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    const header = element.querySelector('header');
    expect(header).toBeTruthy();
    expect(header?.textContent).toContain("Daniel's Dojo");

    expect(element.querySelector('main')).toBeTruthy();
  });

  it('exposes a Home navigation link', () => {
    const { fixture } = setup();
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    const link = element.querySelector('nav a');
    expect(link).toBeTruthy();
    expect(link?.textContent?.trim()).toBe('Home');
  });

  it('always exposes the account link', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nav-account"]')).not.toBeNull();
  });

  it('hides admin navigation from a signed-out visitor', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nav-admin"]')).toBeNull();
  });

  it('hides admin navigation from a Student', () => {
    const { auth, fixture } = setup();
    auth.current.set(student);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nav-admin"]')).toBeNull();
  });

  it('shows admin navigation when the API reported the Admin role', () => {
    const { auth, fixture } = setup();
    auth.current.set({ ...student, roles: ['Student', 'Admin'] });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nav-admin"]')).not.toBeNull();
  });
});
