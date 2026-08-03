import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AppShell } from './app-shell';

describe('AppShell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('creates the shell', () => {
    const fixture = TestBed.createComponent(AppShell);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders header and main landmarks with the product title', () => {
    const fixture = TestBed.createComponent(AppShell);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    const header = element.querySelector('header');
    expect(header).toBeTruthy();
    expect(header?.textContent).toContain("Daniel's Dojo");

    expect(element.querySelector('main')).toBeTruthy();
  });

  it('exposes a Home navigation link', () => {
    const fixture = TestBed.createComponent(AppShell);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;

    const link = element.querySelector('nav a');
    expect(link).toBeTruthy();
    expect(link?.textContent?.trim()).toBe('Home');
  });
});
