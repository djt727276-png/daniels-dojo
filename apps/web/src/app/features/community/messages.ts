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
              <mat-card appearance="outlined">
                <mat-card-content class="conversations__row">
                  <a
                    class="conversations__handle"
                    [routerLink]="['/messages', conversation.id]"
                    [attr.data-testid]="'conversation-' + conversation.id"
                  >
                    {{ conversation.otherHandle }}
                  </a>

                  @if (conversation.unreadCount > 0) {
                    <span class="conversations__unread">
                      {{ conversation.unreadCount }} unread
                    </span>
                  }
                </mat-card-content>
              </mat-card>
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
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .conversations__row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--dd-space-3);
    }

    .conversations__handle {
      flex: 1 1 10rem;
      font-weight: var(--dd-weight-medium);
    }

    .conversations__unread {
      padding: 0.1rem var(--dd-space-3);
      background: var(--dd-info-container);
      color: var(--dd-info);
      border-radius: var(--dd-radius-pill);
      font-size: var(--dd-text-sm);
    }
  `,
})
export class MessageList {
  private readonly api = inject(CommunityApi);
  private readonly realtime = inject(RealtimeService);

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
                  class="conversation__item"
                  [class.conversation__item--own]="message.isOwn"
                  [attr.data-testid]="'message-' + message.id"
                >
                  @if (message.withheld) {
                    <p class="conversation__withheld">This message was deleted.</p>
                  } @else {
                    <pre class="conversation__body">{{ message.body }}</pre>
                  }

                  @if (message.isOwn && !message.withheld) {
                    <button
                      matButton
                      type="button"
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
      max-width: 40rem;
      padding: var(--dd-space-3);
      background: var(--dd-surface-variant);
      border-radius: var(--dd-radius-lg);
    }

    .conversation__item--own {
      align-self: flex-end;
      background: var(--dd-info-container);
    }

    .conversation__body {
      margin: 0;
      font-family: var(--dd-font-sans);
      font-size: var(--dd-text-base);
      line-height: var(--dd-leading-base);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
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
