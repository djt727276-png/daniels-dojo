import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import { AuthService } from '../../core/auth/auth.service';
import {
  FriendRequestPolicy,
  MemberApi,
  MessagePolicy,
  MyCommunityProfile,
} from '../../core/community/member-api';

/**
 * Account page. Renders the session the API reported, offers sign-in and sign-out, and owns
 * the member's community privacy settings.
 *
 * Every displayed value comes from the API; the access token is never decoded in the browser.
 * The email address is shown as profile information only — it is never what identifies the
 * account, so changing it elsewhere would not change who the account belongs to.
 */
@Component({
  selector: 'app-account',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
  ],
  templateUrl: './account.html',
  styleUrl: './account.scss',
})
export class Account {
  private readonly auth = inject(AuthService);
  private readonly members = inject(MemberApi);

  protected readonly state = this.auth.sessionState;
  protected readonly session = this.auth.session;
  protected readonly isConfigured = this.auth.isConfigured;
  protected readonly isAdmin = this.auth.isAdmin;

  protected readonly profile = signal<MyCommunityProfile | null>(null);
  protected readonly profileLoaded = signal(false);
  protected readonly savingPrivacy = signal(false);
  protected readonly privacyMessage = signal<string | null>(null);
  protected readonly privacyError = signal<string | null>(null);

  protected readonly privacyForm = new FormGroup({
    bio: new FormControl('', { nonNullable: true }),
    isDiscoverable: new FormControl(false, { nonNullable: true }),
    friendRequestPolicy: new FormControl<FriendRequestPolicy>('NoOne', { nonNullable: true }),
    messagePolicy: new FormControl<MessagePolicy>('NoOne', { nonNullable: true }),
  });

  constructor() {
    this.loadProfile();
  }

  protected signIn(): void {
    this.auth.signIn();
  }

  protected signOut(): void {
    this.auth.signOut();
  }

  protected retry(): void {
    this.auth.refreshSession();
  }

  /** Loads the community profile. Any failure simply means no settings are shown. */
  protected loadProfile(): void {
    this.members.getCommunityProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.profileLoaded.set(true);
        this.privacyForm.setValue({
          bio: profile.bio ?? '',
          isDiscoverable: profile.isDiscoverable,
          friendRequestPolicy: profile.friendRequestPolicy,
          messagePolicy: profile.messagePolicy,
        });
      },
      error: () => {
        this.profile.set(null);
        this.profileLoaded.set(true);
      },
    });
  }

  protected savePrivacy(): void {
    const current = this.profile();

    if (!current) {
      return;
    }

    const value = this.privacyForm.getRawValue();

    this.savingPrivacy.set(true);
    this.privacyMessage.set(null);
    this.privacyError.set(null);

    this.members
      .updateCommunityProfile({
        bio: value.bio.trim() || null,
        isDiscoverable: value.isDiscoverable,
        friendRequestPolicy: value.friendRequestPolicy,
        messagePolicy: value.messagePolicy,
        rowVersion: current.rowVersion,
      })
      .subscribe({
        next: (profile) => {
          this.savingPrivacy.set(false);
          this.profile.set(profile);
          this.privacyMessage.set('Privacy settings saved.');
        },
        error: (error: unknown) => {
          this.savingPrivacy.set(false);
          this.privacyError.set(
            toApiFailure(error, 'We could not save your privacy settings.').message,
          );
        },
      });
  }
}
