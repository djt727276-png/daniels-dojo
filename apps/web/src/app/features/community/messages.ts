import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { toApiFailure } from '../../core/api/problem-details';
import {
  CommunityApi,
  ConversationDetail,
  ConversationSummary,
  DirectMessageView,
} from '../../core/community/community-api';
import { RealtimeService } from '../../core/community/realtime';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

/** Compact "10:24 AM" for today, "Mon" this week, or a short date otherwise. */
function formatWhen(iso: string): string {
  const date = new Date(iso);
  const now = new Date();
  const dayMs = 86_400_000;
  const sameDay = date.toDateString() === now.toDateString();

  if (sameDay) {
    return new Intl.DateTimeFormat(undefined, { timeStyle: 'short' }).format(date);
  }

  if (now.getTime() - date.getTime() < 6 * dayMs) {
    return new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(date);
  }

  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(date);
}

/** The member's conversation list. */
@Component({
  selector: 'app-message-list',
  imports: [
    RouterLink,
    MatCardModule,
    MatButtonModule,
    PageHeader,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Messages"
        description="Direct messages with members you are friends with."
      >
        <a matButton routerLink="/friends">Friends</a>
      </app-page-header>

      @if (loading()) {
        <app-loading-state message="Loading your messages…" />
      } @else if (failed()) {
        <app-error-state message="We could not load your messages just now." (retry)="load()" />
      } @else if (conversations().length === 0) {
        <app-empty-state
          title="No conversations yet"
          message="Messages are open to friends only, and only when you have both switched them on."
          data-testid="messages-empty"
        >
          <a matButton="filled" routerLink="/friends">See your friends</a>
        </app-empty-state>
      } @else {
        <ul class="conversations" data-testid="conversation-list">
          @for (conversation of conversations(); track conversation.id) {
            <li>
              <a
                class="conversations__row"
                [class.conversations__row--unread]="conversation.unreadCount > 0"
                [routerLink]="['/messages', conversation.id]"
                [attr.data-testid]="'conversation-' + conversation.id"
              >
                <span class="conversations__avatar" aria-hidden="true">
                  {{ conversation.otherHandle.charAt(0).toUpperCase() }}
                </span>
                <span class="conversations__text">
                  <span class="conversations__handle">{{ conversation.otherHandle }}</span>
                  @if (conversation.lastMessageAtUtc; as last) {
                    <span class="conversations__when">{{ when(last) }}</span>
                  }
                </span>
                @if (conversation.unreadCount > 0) {
                  <span class="conversations__unread">
                    {{ conversation.unreadCount }}
                    <span class="dd-visually-hidden">unread</span>
                  </span>
                }
              </a>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .conversations {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .conversations__row {
      display: flex;
      align-items: center;
      gap: var(--dd-space-3);
      padding: var(--dd-space-3) var(--dd-space-4);
      background: var(--dd-surface);
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-md);
      text-decoration: none;
      color: inherit;
      min-height: 3.5rem;
      transition:
        border-color var(--dd-motion-hover) var(--dd-easing-standard),
        background var(--dd-motion-hover) var(--dd-easing-standard);
    }

    .conversations__row:hover {
      border-color: var(--dd-outline-strong);
      background: var(--dd-surface-variant);
      color: inherit;
    }

    .conversations__row--unread .conversations__handle {
      font-weight: var(--dd-weight-bold);
    }

    .conversations__avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.5rem;
      height: 2.5rem;
      flex-shrink: 0;
      border-radius: 50%;
      background: var(--dd-primary-container);
      color: var(--dd-on-primary-container);
      font-weight: var(--dd-weight-semibold);
    }

    .conversations__text {
      display: flex;
      flex-direction: column;
      min-width: 0;
      flex: 1;
    }

    .conversations__handle {
      font-weight: var(--dd-weight-medium);
      overflow-wrap: anywhere;
    }

    .conversations__when {
      font-size: var(--dd-text-xs);
      color: var(--dd-on-surface-variant);
    }

    .conversations__unread {
      min-width: 1.4rem;
      height: 1.4rem;
      padding-inline: 0.35rem;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      background: var(--dd-primary);
      color: var(--dd-on-primary);
      border-radius: var(--dd-radius-pill);
      font-size: var(--dd-text-xs);
      font-weight: var(--dd-weight-semibold);
    }
  `,
})
export class MessageList {
  private readonly api = inject(CommunityApi);
  private readonly realtime = inject(RealtimeService);

  /** Compact relative-or-date label for a conversation's last activity. */
  protected when(iso: string): string {
    return formatWhen(iso);
  }

  protected readonly conversations = signal<readonly ConversationSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  constructor() {
    this.load();

    // The doorbell only says "changed"; the list itself is refetched from REST.
    this.realtime.connect();
    this.realtime.messageReceived.pipe(takeUntilDestroyed()).subscribe(() => this.refresh());
    this.realtime.unreadChanged.pipe(takeUntilDestroyed()).subscribe(() => this.refresh());
    this.realtime.reconnected.pipe(takeUntilDestroyed()).subscribe(() => this.refresh());
  }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);

    this.api.listConversations().subscribe({
      next: (conversations) => {
        this.loading.set(false);
        this.conversations.set(conversations);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  /** Live update: refetch quietly, without blanking the list behind a spinner. */
  private refresh(): void {
    this.api.listConversations().subscribe({
      next: (conversations) => this.conversations.set(conversations),
      error: () => undefined,
    });
  }
}

type ConversationState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly conversation: ConversationDetail }
  | { readonly kind: 'missing' }
  | { readonly kind: 'error'; readonly message: string };

/**
 * One conversation.
 *
 * Message bodies render through a text binding inside a preformatted block, never as HTML.
 * A deleted message arrives with an empty body — the text is gone from the database, not
 * merely hidden here.
 */
@Component({
  selector: 'app-conversation',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    PageHeader,
    LoadingState,
    EmptyState,
    ErrorState,
  ],
  template: `
    <div class="dd-page dd-stack">
      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading the conversation…" />
        }

        @case ('missing') {
          <app-empty-state
            title="Conversation not found"
            message="This conversation is not available to you."
            data-testid="conversation-missing"
          >
            <a matButton="filled" routerLink="/messages">Back to messages</a>
          </app-empty-state>
        }

        @case ('error') {
          <app-error-state [message]="errorMessage()" (retry)="load()" />
        }

        @default {
          @if (conversation(); as current) {
            <app-page-header [title]="current.otherHandle" description="Direct messages.">
              <a matButton routerLink="/messages">Back</a>
            </app-page-header>

            @if (sendError(); as note) {
              <p class="conversation__error" role="alert" data-testid="send-error">{{ note }}</p>
            }

            <ol class="conversation" data-testid="message-list">
              @for (message of messages(); track message.id) {
                <li
                  class="conversation__item dd-enter"
                  [class.conversation__item--own]="message.isOwn"
                  [attr.data-testid]="'message-' + message.id"
                >
                  @if (message.withheld) {
                    <p class="conversation__withheld">This message was deleted.</p>
                  } @else {
                    <!-- Bodies are plain text; fenced blocks the sender wrote render
                         monospace, everything else stays a text binding. -->
                    @for (segment of segments(message.body); track $index) {
                      @if (segment.code) {
                        <pre class="conversation__code"><code>{{ segment.text }}</code></pre>
                      } @else if (segment.text.trim()) {
                        <pre class="conversation__body">{{ segment.text }}</pre>
                      }
                    }
                  }

                  <span class="conversation__meta">
                    {{ when(message.createdAtUtc) }}
                    @if (message.editedAtUtc) {
                      · edited
                    }
                  </span>

                  @if (message.isOwn && !message.withheld) {
                    <button
                      matButton
                      type="button"
                      class="conversation__delete"
                      [disabled]="busy()"
                      (click)="remove(message)"
                      [attr.data-testid]="'delete-' + message.id"
                    >
                      Delete
                    </button>
                  }
                </li>
              }
            </ol>

            @if (current.canSend) {
              <mat-card appearance="outlined">
                <mat-card-content>
                  <form class="dd-stack" [formGroup]="form" (ngSubmit)="send()">
                    <mat-form-field appearance="outline">
                      <mat-label>Your message</mat-label>
                      <textarea
                        matInput
                        rows="3"
                        formControlName="body"
                        data-testid="message-body"
                      ></textarea>
                    </mat-form-field>

                    <div>
                      <button
                        matButton="filled"
                        type="submit"
                        [disabled]="busy()"
                        data-testid="send-message"
                      >
                        Send
                      </button>
                    </div>
                  </form>
                </mat-card-content>
              </mat-card>
            } @else {
              <p class="conversation__closed" data-testid="cannot-send">
                {{ current.cannotSendReason }}
              </p>
            }
          }
        }
      }
    </div>
  `,
  styles: `
    .conversation {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .conversation__item {
      max-width: min(40rem, 85%);
      padding: var(--dd-space-3) var(--dd-space-4);
      background: var(--dd-surface-variant);
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-lg);
      border-bottom-left-radius: var(--dd-radius-sm);
    }

    .conversation__item--own {
      align-self: flex-end;
      background: var(--dd-primary-container);
      border-color: transparent;
      border-bottom-left-radius: var(--dd-radius-lg);
      border-bottom-right-radius: var(--dd-radius-sm);
    }

    .conversation__body {
      margin: 0;
      font-family: var(--dd-font-sans);
      font-size: var(--dd-text-base);
      line-height: var(--dd-leading-base);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .conversation__code {
      margin: var(--dd-space-2) 0;
      padding: var(--dd-space-3);
      background: var(--dd-ink, var(--dd-background));
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-sm);
      font-family: var(--dd-font-mono);
      font-size: var(--dd-text-sm);
      overflow-x: auto;
    }

    .conversation__meta {
      display: block;
      margin-top: var(--dd-space-1);
      font-size: var(--dd-text-xs);
      color: var(--dd-on-surface-variant);
    }

    .conversation__delete {
      margin-top: var(--dd-space-1);
    }

    .conversation__withheld,
    .conversation__closed {
      margin: 0;
      color: var(--dd-on-surface-variant);
    }

    .conversation__error {
      color: var(--dd-danger);
    }
  `,
})
export class Conversation {
  private readonly api = inject(CommunityApi);
  private readonly route = inject(ActivatedRoute);
  private readonly realtime = inject(RealtimeService);

  protected readonly conversationId = this.route.snapshot.paramMap.get('conversationId') ?? '';
  protected readonly state = signal<ConversationState>({ kind: 'loading' });
  protected readonly busy = signal(false);
  protected readonly sendError = signal<string | null>(null);

  protected readonly conversation = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.conversation : null;
  });

  protected readonly messages = computed(() => this.conversation()?.messages.items ?? []);

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  protected readonly form = new FormGroup({
    body: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected when(iso: string): string {
    return formatWhen(iso);
  }

  /** Splits a plain-text body into text and sender-fenced code segments. */
  protected segments(body: string): readonly { text: string; code: boolean }[] {
    const parts = body.split('```');

    // No closing fence — treat the whole body as text.
    if (parts.length < 3) {
      return [{ text: body, code: false }];
    }

    return parts.map((text, index) => ({ text, code: index % 2 === 1 }));
  }

  constructor() {
    this.load();

    // A ring names the conversation that changed; only this one refetches. Reconnection
    // refetches unconditionally — whatever arrived while offline is already persisted.
    this.realtime.connect();
    this.realtime.messageReceived.pipe(takeUntilDestroyed()).subscribe((conversationId) => {
      if (conversationId === this.conversationId) {
        this.refresh();
      }
    });
    this.realtime.reconnected.pipe(takeUntilDestroyed()).subscribe(() => this.refresh());
  }

  /** Live update: refetch quietly, keeping the thread on screen. */
  private refresh(): void {
    this.api.getConversation(this.conversationId).subscribe({
      next: (conversation) => this.state.set({ kind: 'ready', conversation }),
      error: () => undefined,
    });
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.getConversation(this.conversationId).subscribe({
      next: (conversation) => this.state.set({ kind: 'ready', conversation }),
      error: (error: unknown) => {
        const failure = toApiFailure(error, 'We could not load this conversation.');
        this.state.set(
          failure.status === 404
            ? { kind: 'missing' }
            : { kind: 'error', message: failure.message },
        );
      },
    });
  }

  protected send(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.sendError.set(null);

    this.api.sendMessage(this.conversationId, this.form.controls.body.value.trim()).subscribe({
      next: (conversation) => {
        this.busy.set(false);
        this.form.reset({ body: '' });
        this.state.set({ kind: 'ready', conversation });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.sendError.set(toApiFailure(error, 'Your message was not sent.').message);
      },
    });
  }

  protected remove(message: DirectMessageView): void {
    this.busy.set(true);
    this.sendError.set(null);

    this.api.deleteMessage(message.id).subscribe({
      next: (conversation) => {
        this.busy.set(false);
        this.state.set({ kind: 'ready', conversation });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.sendError.set(toApiFailure(error, 'The message was not deleted.').message);
      },
    });
  }
}
