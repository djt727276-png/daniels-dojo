import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { ForumPostView, ForumThreadDetail } from '../../core/community/community-api';
import { ThreadDetail } from './thread-detail';

const THREAD_ID = '11111111-1111-4111-8111-111111111111';
const THREAD_URL = `/api/v1/community/threads/${THREAD_ID}?page=1`;

function post(overrides: Partial<ForumPostView> = {}): ForumPostView {
  return {
    id: 'aaaaaaaa-1111-4111-8111-111111111111',
    replyToPostId: null,
    authorHandle: 'someone',
    authorHidden: false,
    isOwn: false,
    body: 'A perfectly ordinary post.',
    status: 'Published',
    withheld: false,
    withheldReason: null,
    likeCount: 0,
    likedByMe: false,
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    editedAtUtc: null,
    rowVersion: 'AAAAAAAAAAE=',
    ...overrides,
  };
}

function thread(
  posts: readonly ForumPostView[],
  overrides: Partial<ForumThreadDetail> = {},
): ForumThreadDetail {
  return {
    id: THREAD_ID,
    title: 'A thread',
    categorySlug: 'general',
    categoryName: 'General',
    authorHandle: 'someone',
    status: 'Open',
    isPinned: false,
    acceptsReplies: true,
    subscribed: false,
    solvedPostId: null,
    canMarkSolved: false,
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    lastActivityAtUtc: '2026-01-01T00:00:00+00:00',
    posts: { items: posts, page: 1, pageSize: 20, totalCount: posts.length, totalPages: 1 },
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

function setup() {
  TestBed.configureTestingModule({
    imports: [ThreadDetail],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: new Map([['threadId', THREAD_ID]]) } },
      },
    ],
  });

  return {
    fixture: TestBed.createComponent(ThreadDetail),
    http: TestBed.inject(HttpTestingController),
  };
}

function host(fixture: { nativeElement: HTMLElement }): HTMLElement {
  return fixture.nativeElement;
}

describe('ThreadDetail', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('never renders a stored body as HTML', () => {
    const { fixture, http } = setup();

    http
      .expectOne(THREAD_URL)
      .flush(
        thread([post({ body: '<img src=x onerror="alert(1)"><script>alert(2)</script>**bold**' })]),
      );
    fixture.detectChanges();

    const dom = host(fixture);
    const body = dom.querySelector('[data-testid="body-aaaaaaaa-1111-4111-8111-111111111111"]')!;

    expect(body.querySelector('img')).toBeNull();
    expect(body.querySelector('script')).toBeNull();
    expect(dom.querySelector('strong')).toBeNull();
    expect(body.textContent).toContain('<img src=x');
    expect(body.textContent).toContain('**bold**');
  });

  it('shows a placeholder for a withheld post and renders no body element', () => {
    const { fixture, http } = setup();

    http.expectOne(THREAD_URL).flush(
      thread([
        post({
          body: '',
          withheld: true,
          withheldReason: 'Hidden because of a block.',
          authorHidden: true,
          authorHandle: 'Hidden member',
        }),
      ]),
    );
    fixture.detectChanges();

    const dom = host(fixture);
    expect(
      dom.querySelector('[data-testid="withheld-aaaaaaaa-1111-4111-8111-111111111111"]')
        ?.textContent,
    ).toContain('Hidden because of a block.');
    expect(
      dom.querySelector('[data-testid="body-aaaaaaaa-1111-4111-8111-111111111111"]'),
    ).toBeNull();
    expect(dom.textContent).toContain('Hidden member');
  });

  it('offers edit and remove only on the reader’s own post', () => {
    const { fixture, http } = setup();

    http.expectOne(THREAD_URL).flush(thread([post({ isOwn: true })]));
    fixture.detectChanges();

    const dom = host(fixture);
    expect(
      dom.querySelector('[data-testid="edit-aaaaaaaa-1111-4111-8111-111111111111"]'),
    ).not.toBeNull();
    expect(
      dom.querySelector('[data-testid="report-aaaaaaaa-1111-4111-8111-111111111111"]'),
    ).toBeNull();
  });

  it('offers the answer buttons only to the thread author and never on the opening post', () => {
    const { fixture, http } = setup();

    const opening = post();
    const reply = post({ id: 'bbbbbbbb-1111-4111-8111-111111111111' });

    http.expectOne(THREAD_URL).flush(thread([opening, reply], { canMarkSolved: true }));
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector(`[data-testid="mark-solved-${opening.id}"]`)).toBeNull();
    expect(dom.querySelector(`[data-testid="mark-solved-${reply.id}"]`)).not.toBeNull();
  });

  it('badges the accepted answer for every reader', () => {
    const { fixture, http } = setup();

    const reply = post({ id: 'bbbbbbbb-1111-4111-8111-111111111111' });

    http
      .expectOne(THREAD_URL)
      .flush(thread([post(), reply], { solvedPostId: reply.id, canMarkSolved: false }));
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector(`[data-testid="solution-${reply.id}"]`)).not.toBeNull();
    expect(dom.querySelector(`[data-testid="mark-solved-${reply.id}"]`)).toBeNull();
    expect(dom.textContent).toContain('Accepted answer');
  });

  it('hides the reply form when the thread is closed', () => {
    const { fixture, http } = setup();

    http.expectOne(THREAD_URL).flush(thread([post()], { status: 'Locked', acceptsReplies: false }));
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector('[data-testid="post-reply"]')).toBeNull();
    expect(dom.querySelector('[data-testid="replies-closed"]')).not.toBeNull();
  });

  it('sends the row version it holds when editing', () => {
    const { fixture, http } = setup();

    http.expectOne(THREAD_URL).flush(thread([post({ isOwn: true })]));
    fixture.detectChanges();

    const dom = host(fixture);
    dom
      .querySelector<HTMLButtonElement>(
        '[data-testid="edit-aaaaaaaa-1111-4111-8111-111111111111"]',
      )!
      .click();
    fixture.detectChanges();

    const textarea = dom.querySelector<HTMLTextAreaElement>(
      '[data-testid="edit-body-aaaaaaaa-1111-4111-8111-111111111111"]',
    )!;
    textarea.value = 'Edited text.';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    dom.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));

    const request = http.expectOne('/api/v1/community/posts/aaaaaaaa-1111-4111-8111-111111111111');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ body: 'Edited text.', rowVersion: 'AAAAAAAAAAE=' });

    request.flush(thread([post({ isOwn: true, body: 'Edited text.' })]));
  });

  it('reports a missing thread without hinting that it was removed', () => {
    const { fixture, http } = setup();

    http.expectOne(THREAD_URL).flush('', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector('[data-testid="thread-missing"]')).not.toBeNull();
    expect(dom.textContent).not.toContain('removed');
    expect(dom.textContent).not.toContain('moderator');
  });
});
