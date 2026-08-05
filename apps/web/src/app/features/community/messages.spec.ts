import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { Subject } from 'rxjs';

import { ConversationDetail, DirectMessageView } from '../../core/community/community-api';
import { RealtimeService } from '../../core/community/realtime';
import { Conversation, MessageList } from './messages';

/** A silent doorbell: no connection is opened, and tests can ring it on demand. */
function realtimeStub() {
  return {
    messageReceived: new Subject<string>(),
    unreadChanged: new Subject<void>(),
    reconnected: new Subject<void>(),
    connect: () => undefined,
  };
}

const CONVERSATION_ID = '11111111-1111-4111-8111-111111111111';
const CONVERSATION_URL = `/api/v1/community/conversations/${CONVERSATION_ID}?page=1`;
const LIST_URL = '/api/v1/community/conversations';

function message(overrides: Partial<DirectMessageView> = {}): DirectMessageView {
  return {
    id: 'aaaaaaaa-1111-4111-8111-111111111111',
    isOwn: false,
    body: 'An ordinary message.',
    status: 'Sent',
    withheld: false,
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    editedAtUtc: null,
    rowVersion: 'AAAAAAAAAAE=',
    ...overrides,
  };
}

function conversation(
  messages: readonly DirectMessageView[],
  overrides: Partial<ConversationDetail> = {},
): ConversationDetail {
  return {
    id: CONVERSATION_ID,
    otherUserId: '22222222-2222-4222-8222-222222222222',
    otherHandle: 'friendly-member',
    canSend: true,
    cannotSendReason: null,
    messages: {
      items: messages,
      page: 1,
      pageSize: 30,
      totalCount: messages.length,
      totalPages: 1,
    },
    ...overrides,
  };
}

function setupConversation() {
  const realtime = realtimeStub();

  TestBed.configureTestingModule({
    imports: [Conversation],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: RealtimeService, useValue: realtime },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: new Map([['conversationId', CONVERSATION_ID]]) } },
      },
    ],
  });

  return {
    fixture: TestBed.createComponent(Conversation),
    http: TestBed.inject(HttpTestingController),
    realtime,
  };
}

function host(fixture: { nativeElement: HTMLElement }): HTMLElement {
  return fixture.nativeElement;
}

describe('Conversation', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('never renders a message body as HTML', () => {
    const { fixture, http } = setupConversation();

    http
      .expectOne(CONVERSATION_URL)
      .flush(conversation([message({ body: '<img src=x onerror="alert(1)">**not bold**' })]));
    fixture.detectChanges();

    const dom = host(fixture);
    const body = dom.querySelector('.conversation__body')!;

    expect(body.querySelector('img')).toBeNull();
    expect(dom.querySelector('strong')).toBeNull();
    expect(body.textContent).toContain('<img src=x');
    expect(body.textContent).toContain('**not bold**');
  });

  it('shows a placeholder for a deleted message and renders no body', () => {
    const { fixture, http } = setupConversation();

    http
      .expectOne(CONVERSATION_URL)
      .flush(conversation([message({ body: '', status: 'Deleted', withheld: true, isOwn: true })]));
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector('.conversation__body')).toBeNull();
    expect(dom.textContent).toContain('This message was deleted.');

    // A tombstone offers no delete action, because there is nothing left to delete.
    expect(
      dom.querySelector('[data-testid="delete-aaaaaaaa-1111-4111-8111-111111111111"]'),
    ).toBeNull();
  });

  it('offers delete only on the reader’s own message', () => {
    const { fixture, http } = setupConversation();

    http.expectOne(CONVERSATION_URL).flush(conversation([message({ isOwn: false })]));
    fixture.detectChanges();

    expect(
      host(fixture).querySelector('[data-testid="delete-aaaaaaaa-1111-4111-8111-111111111111"]'),
    ).toBeNull();
  });

  it('replaces the composer with the server’s reason when sending is closed', () => {
    const { fixture, http } = setupConversation();

    http.expectOne(CONVERSATION_URL).flush(
      conversation([message()], {
        canSend: false,
        cannotSendReason: 'You cannot message this member.',
        otherHandle: 'Hidden member',
      }),
    );
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector('[data-testid="send-message"]')).toBeNull();
    expect(dom.querySelector('[data-testid="cannot-send"]')?.textContent).toContain(
      'You cannot message this member.',
    );

    // A blocked counterpart is not named anywhere on the page.
    expect(dom.textContent).toContain('Hidden member');
    expect(dom.textContent).not.toContain('friendly-member');
  });

  it('shows a recoverable error rather than an empty page when loading fails', () => {
    const { fixture, http } = setupConversation();

    http.expectOne(CONVERSATION_URL).flush('', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(host(fixture).textContent).toContain('could not load this conversation');
  });

  it('refetches when the doorbell rings for this conversation, and only this one', () => {
    const { fixture, http, realtime } = setupConversation();

    http.expectOne(CONVERSATION_URL).flush(conversation([message()]));
    fixture.detectChanges();

    // A ring for some other conversation is ignored — no request goes out.
    realtime.messageReceived.next('99999999-9999-4999-8999-999999999999');
    http.expectNone(CONVERSATION_URL);

    // A ring for this conversation refetches the same REST endpoint.
    realtime.messageReceived.next(CONVERSATION_ID);
    http
      .expectOne(CONVERSATION_URL)
      .flush(conversation([message(), message({ id: 'bbbbbbbb-1111-4111-8111-111111111111' })]));
    fixture.detectChanges();

    expect(host(fixture).querySelectorAll('.conversation__item').length).toBe(2);
  });

  it('refetches after a reconnect, because missed events are already persisted', () => {
    const { fixture, http, realtime } = setupConversation();

    http.expectOne(CONVERSATION_URL).flush(conversation([]));
    fixture.detectChanges();

    realtime.reconnected.next();
    http.expectOne(CONVERSATION_URL).flush(conversation([message()]));
    fixture.detectChanges();

    expect(host(fixture).querySelectorAll('.conversation__item').length).toBe(1);
  });

  it('reports a foreign conversation as simply unavailable', () => {
    const { fixture, http } = setupConversation();

    http.expectOne(CONVERSATION_URL).flush('', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector('[data-testid="conversation-missing"]')).not.toBeNull();
    expect(dom.textContent).not.toContain('blocked');
    expect(dom.textContent).not.toContain('forbidden');
  });
});

describe('MessageList', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  function setupList() {
    const realtime = realtimeStub();

    TestBed.configureTestingModule({
      imports: [MessageList],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RealtimeService, useValue: realtime },
      ],
    });

    return {
      fixture: TestBed.createComponent(MessageList),
      http: TestBed.inject(HttpTestingController),
      realtime,
    };
  }

  it('explains why the list is empty instead of looking broken', () => {
    const { fixture, http } = setupList();

    http.expectOne(LIST_URL).flush([]);
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.querySelector('[data-testid="messages-empty"]')).not.toBeNull();
    expect(dom.textContent).toContain('friends only');
  });

  it('shows a blocked counterpart as hidden rather than by handle', () => {
    const { fixture, http } = setupList();

    http.expectOne(LIST_URL).flush([
      {
        id: CONVERSATION_ID,
        otherUserId: '22222222-2222-4222-8222-222222222222',
        otherHandle: 'Hidden member',
        otherHidden: true,
        lastMessageAtUtc: '2026-01-01T00:00:00+00:00',
        unreadCount: 2,
      },
    ]);
    fixture.detectChanges();

    const dom = host(fixture);
    expect(dom.textContent).toContain('Hidden member');
    expect(dom.textContent).toContain('2 unread');
  });
});
