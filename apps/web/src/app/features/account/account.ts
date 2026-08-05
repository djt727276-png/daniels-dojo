import { Component, inject, signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
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
import { Avatar } from '../../shared/ui/avatar/avatar';
import { ConfirmDialog, ConfirmDialogResult } from '../../shared/ui/confirm-dialog/confirm-dialog';

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
    Avatar,
  ],
  templateUrl: './account.html',
  styleUrl: './account.scss',
})
export class Account {
  private readonly auth = inject(AuthService);
  private readonly members = inject(MemberApi);
  private readonly dialog = inject(MatDialog);

  protected readonly state = this.auth.sessionState;
  protected readonly session = this.auth.session;
  protected readonly isConfigured = this.auth.isConfigured;
  protected readonly isAdmin = this.auth.isAdmin;

  protected readonly profile = signal<MyCommunityProfile | null>(null);
  protected readonly profileLoaded = signal(false);
  protected readonly savingPrivacy = signal(false);
  protected readonly privacyMessage = signal<string | null>(null);
  protected readonly privacyError = signal<string | null>(null);
  protected readonly savingAvatar = signal(false);
  protected readonly avatarError = signal<string | null>(null);
  protected readonly avatarVersion = signal(0);
  protected readonly busyWithData = signal(false);
  protected readonly privacyDataError = signal<string | null>(null);

  protected readonly privacyForm = new FormGroup({
    bio: new FormControl('', { nonNullable: true }),
    isDiscoverable: new FormControl(false, { nonNullable: true }),
    friendRequestPolicy: new FormControl<FriendRequestPolicy>('NoOne', { nonNullable: true }),
    messagePolicy: new FormControl<MessagePolicy>('NoOne', { nonNullable: true }),
  });

  constructor() {
    this.loadProfile();
  }

  protected createAccount(): void {
    this.auth.createAccount();
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

  protected uploadAvatar(event: Event): void {
    const inputElement = event.target as HTMLInputElement;
    const file = inputElement.files?.[0];
    inputElement.value = '';

    if (!file) {
      return;
    }

    this.savingAvatar.set(true);
    this.avatarError.set(null);

    this.members.uploadAvatar(file).subscribe({
      next: () => {
        this.savingAvatar.set(false);
        this.avatarVersion.update((version) => version + 1);
        this.loadProfile();
      },
      error: (error: unknown) => {
        this.savingAvatar.set(false);
        this.avatarError.set(toApiFailure(error, 'That photo could not be used.').message);
      },
    });
  }

  protected removeAvatar(): void {
    this.savingAvatar.set(true);
    this.avatarError.set(null);

    this.members.removeAvatar().subscribe({
      next: () => {
        this.savingAvatar.set(false);
        this.avatarVersion.update((version) => version + 1);
        this.loadProfile();
      },
      error: (error: unknown) => {
        this.savingAvatar.set(false);
        this.avatarError.set(toApiFailure(error, 'The photo was not removed.').message);
      },
    });
  }

  protected downloadData(): void {
    this.busyWithData.set(true);
    this.privacyDataError.set(null);

    this.members.exportMyData().subscribe({
      next: (blob) => {
        this.busyWithData.set(false);

        // An ordinary browser download of the member's own copy.
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'daniels-dojo-my-data.json';
        anchor.click();
        URL.revokeObjectURL(url);
      },
      error: (error: unknown) => {
        this.busyWithData.set(false);
        this.privacyDataError.set(
          toApiFailure(error, 'Your data could not be exported just now.').message,
        );
      },
    });
  }

  protected deleteAccount(): void {
    this.dialog
      .open<ConfirmDialog, unknown, ConfirmDialogResult>(ConfirmDialog, {
        data: {
          title: 'Delete your account?',
          message:
            'This is immediate and cannot be undone. Your community profile, photo, ' +
            'friendships, and messages are removed; payment records we must keep by law ' +
            'are kept without your name. Type "delete my account" to confirm.',
          confirmLabel: 'Delete my account',
          destructive: true,
          requireReason: true,
          reasonLabel: 'Type: delete my account',
        },
        width: '32rem',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }

        this.busyWithData.set(true);
        this.privacyDataError.set(null);

        this.members.deleteMyAccount(result.reason).subscribe({
          next: () => {
            // The account is gone; end the session cleanly.
            this.signOut();
          },
          error: (error: unknown) => {
            this.busyWithData.set(false);
            this.privacyDataError.set(toApiFailure(error, 'Your account was not deleted.').message);
          },
        });
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
