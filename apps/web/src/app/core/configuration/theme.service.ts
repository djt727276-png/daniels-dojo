import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject, signal } from '@angular/core';

/** The visitor's theme choice. `system` follows the operating system. */
export type ThemePreference = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'dd-theme';

/**
 * Owns the light/dark/system choice.
 *
 * The choice is a `data-theme` attribute on `<html>`; every colour in the product is a role
 * token that responds to that attribute (or, for `system`, to the OS preference), so no
 * component participates in theming at all. The preference persists locally — it is a device
 * preference, not account data, and must work signed out.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  /** The stored preference, defaulting to following the operating system. */
  readonly preference = signal<ThemePreference>(readStored());

  constructor() {
    effect(() => {
      const value = this.preference();
      const root = this.document.documentElement;

      if (value === 'system') {
        // No attribute: the media query in the stylesheet decides.
        root.removeAttribute('data-theme');
      } else {
        root.setAttribute('data-theme', value);
      }

      try {
        localStorage.setItem(STORAGE_KEY, value);
      } catch {
        // Storage can be unavailable (private windows, quota). The theme still applies for
        // this visit; it simply will not be remembered.
      }
    });
  }

  /** Cycles system → dark → light → system, for a single toolbar control. */
  cycle(): void {
    const order: readonly ThemePreference[] = ['system', 'dark', 'light'];
    const next = order[(order.indexOf(this.preference()) + 1) % order.length];

    this.preference.set(next);
  }

  /** Icon name describing the current preference. */
  icon(): string {
    switch (this.preference()) {
      case 'dark':
        return 'dark_mode';
      case 'light':
        return 'light_mode';
      default:
        return 'contrast';
    }
  }

  /** Accessible label describing what activating the control will do. */
  nextLabel(): string {
    switch (this.preference()) {
      case 'system':
        return 'Switch to dark theme';
      case 'dark':
        return 'Switch to light theme';
      default:
        return 'Match the system theme';
    }
  }
}

function readStored(): ThemePreference {
  try {
    const value = localStorage.getItem(STORAGE_KEY);

    return value === 'light' || value === 'dark' || value === 'system' ? value : 'system';
  } catch {
    return 'system';
  }
}
