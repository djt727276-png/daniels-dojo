import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { toApiFailure } from '../../core/api/problem-details';
import {
  CommunityApi,
  ForumPostView,
  ForumThreadDetail,
  REPORT_REASONS,
} from '../../core/community/community-api';
import { ConfirmDialog } from '../../shared/ui/confirm-dialog/confirm-dialog';
import {
  FormErrorEntry,
  FormErrorSummary,
} from '../../shared/ui/form-error-summary/form-error-summary';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';
import { StatusChip } from '../../shared/ui/status-chip/status-chip';

type ThreadState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly thread: ForumThreadDetail }
  | { readonly kind: 'missing' }
  | { readonly kind: 'error'; readonly message: string };

/**
 * One thread: its posts, replying, reactions, subscription, and reporting.
 *
 * Post bodies are rendered through a text binding inside a preformatted block. They are never
 * passed to `innerHTML` and no Markdown renderer exists in the project, so stored text cannot
 * become executable markup in another member's browser.
 */
@Component({
  selector: 'app-thread-detail',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    PageHeader,
    StatusChip,
    LoadingState,
    EmptyState,
    ErrorState,
    FormErrorSummary,
  ],
  templateUrl: './thread-detail.html',
  styleUrl: './thread-detail.scss',
})
export class ThreadDetail {
  private readonly api = inject(CommunityApi);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);

  protected readonly reasons = REPORT_REASONS;
  protected readonly threadId = this.route.snapshot.paramMap.get('threadId') ?? '';
  protected readonly state = signal<ThreadState>({ kind: 'loading' });
  protected readonly busy = signal(false);
  protected readonly errors = signal<readonly FormErrorEntry[]>([]);
  protected readonly editingPostId = signal<string | null>(null);

  protected readonly thread = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.thread : null;
  });

  protected readonly posts = computed(() => this.thread()?.posts.items ?? []);

  protected readonly errorMessage = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  protected readonly replyForm = new FormGroup({
    body: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly editForm = new FormGroup({
    body: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.getThread(this.threadId).subscribe({
      next: (thread) => this.state.set({ kind: 'ready', thread }),
      error: (error: unknown) => {
        const failure = toApiFailure(error, 'We could not load this thread.');
        this.state.set(
          failure.status === 404
            ? { kind: 'missing' }
            : { kind: 'error', message: failure.message },
        );
      },
    });
  }

  protected reply(): void {
    this.replyForm.markAllAsTouched();

    if (this.replyForm.invalid) {
      this.errors.set([{ field: 'body', message: 'Write a reply before posting.' }]);
      return;
    }

    this.run(
      this.api.createPost(this.threadId, this.replyForm.controls.body.value.trim(), null),
      () => this.replyForm.reset({ body: '' }),
    );
  }

  protected startEdit(post: ForumPostView): void {
    this.editingPostId.set(post.id);
    this.editForm.setValue({ body: post.body });
  }

  protected cancelEdit(): void {
    this.editingPostId.set(null);
  }

  protected saveEdit(post: ForumPostView): void {
    if (this.editForm.invalid) {
      this.editForm.markAllAsTouched();
      return;
    }

    this.run(
      this.api.updatePost(post.id, this.editForm.controls.body.value.trim(), post.rowVersion),
      () => this.editingPostId.set(null),
    );
  }

  protected removeOwn(post: ForumPostView): void {
    this.dialog
      .open(ConfirmDialog, {
        data: {
          title: 'Remove your post?',
          message:
            'The post is replaced with a placeholder so replies keep their place. The text is deleted.',
          confirmLabel: 'Remove',
          destructive: true,
        },
        width: '32rem',
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.run(this.api.removeOwnPost(post.id));
        }
      });
  }

  protected toggleLike(post: ForumPostView): void {
    this.run(this.api.setReaction(post.id, !post.likedByMe));
  }

  protected markSolved(post: ForumPostView): void {
    this.run(this.api.setSolved(this.threadId, post.id));
  }

  protected clearSolved(): void {
    this.run(this.api.setSolved(this.threadId, null));
  }

  protected toggleSubscription(): void {
    const thread = this.thread();

    if (thread) {
      this.run(this.api.setSubscription(thread.id, !thread.subscribed));
    }
  }

  protected reportPost(post: ForumPostView): void {
    this.dialog
      .open(ConfirmDialog, {
        data: {
          title: 'Report this post?',
          message:
            'A moderator will review it. Tell them what is wrong — your note goes to the moderation queue.',
          confirmLabel: 'Send report',
          requireReason: true,
          reasonLabel: 'What is wrong with this post?',
        },
        width: '32rem',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }

        this.busy.set(true);
        this.api.report('Post', post.id, 'Other', result.reason).subscribe({
          next: () => {
            this.busy.set(false);
            this.errors.set([]);
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.errors.set([
              { field: 'body', message: toApiFailure(error, 'The report was not sent.').message },
            ]);
          },
        });
      });
  }

  private run(request: Observable<ForumThreadDetail>, onSuccess?: () => void): void {
    this.busy.set(true);
    this.errors.set([]);

    request.subscribe({
      next: (thread) => {
        this.busy.set(false);
        onSuccess?.();
        this.state.set({ kind: 'ready', thread });
      },
      error: (error: unknown) => {
        this.busy.set(false);

        const failure = toApiFailure(error, 'That could not be saved.');
        this.errors.set(
          failure.fieldErrors.length > 0
            ? failure.fieldErrors
            : [{ field: 'body', message: failure.message }],
        );
      },
    });
  }
}
