import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';

import { CommunityApi } from '../../../core/community/community-api';

/**
 * A member's avatar, or their initial while there is none.
 *
 * The image is fetched through the authenticated HTTP client rather than an `<img src>`
 * URL, so the bearer token travels with the request and the server's block rules apply.
 * The received bytes become a short-lived object URL that is revoked when the component
 * goes away or the member changes.
 */
@Component({
  selector: 'app-avatar',
  template: `
    @if (objectUrl(); as url) {
      <img class="avatar__image" [src]="url" [alt]="handle() + ' avatar'" />
    } @else {
      <span class="avatar__initial" aria-hidden="true">{{ initial() }}</span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      inline-size: var(--dd-avatar-size, 2.5rem);
      block-size: var(--dd-avatar-size, 2.5rem);
      flex: none;
      overflow: hidden;
      border-radius: 50%;
      background: var(--dd-surface-variant);
      color: var(--dd-on-surface-variant);
    }

    .avatar__image {
      inline-size: 100%;
      block-size: 100%;
      object-fit: cover;
    }

    .avatar__initial {
      font-size: calc(var(--dd-avatar-size, 2.5rem) * 0.45);
      font-weight: var(--dd-weight-medium);
      text-transform: uppercase;
    }
  `,
})
export class Avatar {
  private readonly api = inject(CommunityApi);
  private readonly destroyRef = inject(DestroyRef);

  /** Whose avatar. */
  readonly userId = input.required<string>();

  /** Handle, for the alt text and the fallback initial. */
  readonly handle = input.required<string>();

  /** Whether the server says this member has one; false skips the fetch entirely. */
  readonly hasAvatar = input(false);

  /** Bump to refetch after the member replaces their photo. */
  readonly version = input(0);

  protected readonly objectUrl = signal<string | null>(null);

  protected readonly initial = computed(() => this.handle().slice(0, 1) || '?');

  constructor() {
    this.destroyRef.onDestroy(() => this.revoke());

    effect(() => {
      const userId = this.userId();
      const wanted = this.hasAvatar();
      this.version();

      this.revoke();

      if (!wanted) {
        return;
      }

      this.api.getAvatar(userId).subscribe({
        next: (blob) => {
          this.revoke();
          this.objectUrl.set(URL.createObjectURL(blob));
        },
        // Hidden or missing renders exactly like "no avatar".
        error: () => this.objectUrl.set(null),
      });
    });
  }

  private revoke(): void {
    const url = this.objectUrl();

    if (url) {
      URL.revokeObjectURL(url);
      this.objectUrl.set(null);
    }
  }
}
