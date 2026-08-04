import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResult } from '../catalog/catalog.model';
import { API_BASE_PATH } from '../configuration/app-config';

/** Lifecycle of a forum thread. */
export type ThreadStatus = 'Open' | 'Locked' | 'Archived' | 'Removed';

/** A forum category with a live thread count. */
export interface ForumCategorySummary {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly description: string;
  readonly sortOrder: number;
  readonly threadCount: number;
  readonly lastActivityAtUtc: string | null;
}

/** A thread as it appears in a category listing. */
export interface ForumThreadSummary {
  readonly id: string;
  readonly title: string;
  readonly categorySlug: string;
  readonly authorHandle: string;
  readonly authorHidden: boolean;
  readonly status: ThreadStatus;
  readonly isPinned: boolean;
  readonly replyCount: number;
  readonly createdAtUtc: string;
  readonly lastActivityAtUtc: string;
}

/** A post as a reader sees it. Withheld bodies arrive empty, never hidden client-side. */
export interface ForumPostView {
  readonly id: string;
  readonly replyToPostId: string | null;
  readonly authorHandle: string;
  readonly authorHidden: boolean;
  readonly isOwn: boolean;
  readonly body: string;
  readonly status: string;
  readonly withheld: boolean;
  readonly withheldReason: string | null;
  readonly likeCount: number;
  readonly likedByMe: boolean;
  readonly createdAtUtc: string;
  readonly editedAtUtc: string | null;
  readonly rowVersion: string;
}

/** A thread with a page of its posts. */
export interface ForumThreadDetail {
  readonly id: string;
  readonly title: string;
  readonly categorySlug: string;
  readonly categoryName: string;
  readonly authorHandle: string;
  readonly status: ThreadStatus;
  readonly isPinned: boolean;
  readonly acceptsReplies: boolean;
  readonly subscribed: boolean;
  readonly createdAtUtc: string;
  readonly lastActivityAtUtc: string;
  readonly posts: PagedResult<ForumPostView>;
  readonly rowVersion: string;
}

/** Another member as this viewer is allowed to see them. */
export interface MemberCard {
  readonly userId: string;
  readonly handle: string;
  readonly bio: string | null;
  readonly isFriend: boolean;
  readonly requestPending: boolean;
  readonly canReceiveFriendRequests: boolean;
  readonly canReceiveMessages: boolean;
}

/** A pending friend request. */
export interface FriendRequestView {
  readonly id: string;
  readonly otherUserId: string;
  readonly otherHandle: string;
  readonly incoming: boolean;
  readonly requestedAtUtc: string;
  readonly rowVersion: string;
}

/** An accepted friendship. */
export interface FriendView {
  readonly userId: string;
  readonly handle: string;
  readonly acceptedAtUtc: string;
}

/** A member this viewer has blocked. */
export interface BlockView {
  readonly userId: string;
  readonly handle: string;
  readonly reasonCategory: string;
  readonly createdAtUtc: string;
}

/** A direct conversation summary. */
export interface ConversationSummary {
  readonly id: string;
  readonly otherUserId: string;
  readonly otherHandle: string;
  readonly otherHidden: boolean;
  readonly lastMessageAtUtc: string | null;
  readonly unreadCount: number;
}

/** One direct message. */
export interface DirectMessageView {
  readonly id: string;
  readonly isOwn: boolean;
  readonly body: string;
  readonly status: string;
  readonly withheld: boolean;
  readonly createdAtUtc: string;
  readonly editedAtUtc: string | null;
  readonly rowVersion: string;
}

/** A conversation with a page of messages. */
export interface ConversationDetail {
  readonly id: string;
  readonly otherUserId: string;
  readonly otherHandle: string;
  readonly canSend: boolean;
  readonly cannotSendReason: string | null;
  readonly messages: PagedResult<DirectMessageView>;
}

/** An entry in the notification inbox. Carries a pointer, never content. */
export interface NotificationView {
  readonly id: string;
  readonly kind: string;
  readonly actorHandle: string | null;
  readonly targetType: string;
  readonly targetId: string;
  readonly createdAtUtc: string;
  readonly read: boolean;
}

/** A moderation queue entry. */
export interface ModerationReport {
  readonly id: string;
  readonly targetType: string;
  readonly targetId: string;
  readonly reasonCode: string;
  readonly detail: string | null;
  readonly status: string;
  readonly reporterHandle: string;
  readonly handledByDisplayName: string | null;
  readonly resolution: string | null;
  readonly createdAtUtc: string;
  readonly handledAtUtc: string | null;
  readonly rowVersion: string;
}

/**
 * The reported item itself, unlocked by an open report.
 *
 * This is the only route to a private message's text, it returns one target and nothing
 * around it, and the server audits every read.
 */
export interface ModerationTarget {
  readonly reportId: string;
  readonly targetType: string;
  readonly targetId: string;
  readonly authorHandle: string;
  readonly status: string;
  readonly content: string;
  readonly context: string | null;
  readonly createdAtUtc: string;
}

/** One recent privileged action, as the Admin landing page shows it. */
export interface AuditActivityEntry {
  readonly id: string;
  readonly action: string;
  readonly targetType: string;
  readonly targetId: string;
  readonly actorDisplayName: string;
  readonly reason: string | null;
  readonly occurredAtUtc: string;
}

/** Everything the Admin landing page needs, in one round trip. */
export interface AdminOverview {
  readonly draftCourses: number;
  readonly publishedCourses: number;
  readonly archivedCourses: number;
  readonly coursesReadyToPublish: number;
  readonly activeOffers: number;
  readonly draftOffers: number;
  readonly openReports: number;
  readonly reviewingReports: number;
  readonly forumCategories: number;
  readonly recentActivity: readonly AuditActivityEntry[];
}

/** A forum category as an operator manages it, including archived ones. */
export interface AdminForumCategory {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly description: string;
  readonly sortOrder: number;
  readonly status: 'Active' | 'Archived';
  readonly threadCount: number;
  readonly rowVersion: string;
}

/** Reasons a member can choose when reporting. */
export const REPORT_REASONS = [
  'Spam',
  'Harassment',
  'Hate',
  'SexualContent',
  'Violence',
  'Impersonation',
  'Privacy',
  'Other',
] as const;

/** Typed client for the community endpoints. */
@Injectable({ providedIn: 'root' })
export class CommunityApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_PATH);
  private readonly root = `${this.base}/v1/community`;

  // ---------------------------------------------------------------- forum

  listCategories(): Observable<readonly ForumCategorySummary[]> {
    return this.http.get<readonly ForumCategorySummary[]>(`${this.root}/categories`);
  }

  listThreads(categorySlug: string, page = 1): Observable<PagedResult<ForumThreadSummary>> {
    return this.http.get<PagedResult<ForumThreadSummary>>(
      `${this.root}/categories/${encodeURIComponent(categorySlug)}/threads`,
      { params: new HttpParams().set('page', page) },
    );
  }

  listRecentThreads(count = 5): Observable<readonly ForumThreadSummary[]> {
    return this.http.get<readonly ForumThreadSummary[]>(`${this.root}/threads/recent`, {
      params: new HttpParams().set('count', count),
    });
  }

  getThread(threadId: string, page = 1): Observable<ForumThreadDetail> {
    return this.http.get<ForumThreadDetail>(`${this.root}/threads/${threadId}`, {
      params: new HttpParams().set('page', page),
    });
  }

  createThread(categorySlug: string, title: string, body: string): Observable<ForumThreadDetail> {
    return this.http.post<ForumThreadDetail>(`${this.root}/threads`, {
      categorySlug,
      title,
      body,
    });
  }

  createPost(
    threadId: string,
    body: string,
    replyToPostId: string | null,
  ): Observable<ForumThreadDetail> {
    return this.http.post<ForumThreadDetail>(`${this.root}/threads/${threadId}/posts`, {
      body,
      replyToPostId,
    });
  }

  updatePost(postId: string, body: string, rowVersion: string): Observable<ForumThreadDetail> {
    return this.http.put<ForumThreadDetail>(`${this.root}/posts/${postId}`, { body, rowVersion });
  }

  removeOwnPost(postId: string): Observable<ForumThreadDetail> {
    return this.http.delete<ForumThreadDetail>(`${this.root}/posts/${postId}`);
  }

  setReaction(postId: string, liked: boolean): Observable<ForumThreadDetail> {
    return this.http.put<ForumThreadDetail>(`${this.root}/posts/${postId}/reaction`, { liked });
  }

  setSubscription(threadId: string, subscribed: boolean): Observable<ForumThreadDetail> {
    return this.http.put<ForumThreadDetail>(`${this.root}/threads/${threadId}/subscription`, {
      subscribed,
    });
  }

  report(
    targetType: string,
    targetId: string,
    reasonCode: string,
    detail: string | null,
  ): Observable<void> {
    return this.http.post<void>(`${this.root}/reports`, {
      targetType,
      targetId,
      reasonCode,
      detail,
    });
  }

  // ---------------------------------------------------------------- people

  searchMembers(search: string): Observable<readonly MemberCard[]> {
    return this.http.get<readonly MemberCard[]>(`${this.root}/people`, {
      params: new HttpParams().set('search', search),
    });
  }

  getMember(handle: string): Observable<MemberCard> {
    return this.http.get<MemberCard>(`${this.root}/people/${encodeURIComponent(handle)}`);
  }

  listFriends(): Observable<readonly FriendView[]> {
    return this.http.get<readonly FriendView[]>(`${this.root}/friends`);
  }

  removeFriend(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.root}/friends/${userId}`);
  }

  listFriendRequests(): Observable<readonly FriendRequestView[]> {
    return this.http.get<readonly FriendRequestView[]>(`${this.root}/friend-requests`);
  }

  sendFriendRequest(handle: string): Observable<void> {
    return this.http.post<void>(`${this.root}/friend-requests`, { handle });
  }

  respondToFriendRequest(
    requestId: string,
    action: 'accept' | 'decline' | 'cancel',
  ): Observable<void> {
    return this.http.post<void>(`${this.root}/friend-requests/${requestId}/${action}`, {});
  }

  listBlocks(): Observable<readonly BlockView[]> {
    return this.http.get<readonly BlockView[]>(`${this.root}/blocks`);
  }

  block(handle: string, reasonCategory: string): Observable<void> {
    return this.http.post<void>(`${this.root}/blocks`, { handle, reasonCategory });
  }

  unblock(userId: string): Observable<void> {
    return this.http.delete<void>(`${this.root}/blocks/${userId}`);
  }

  // ---------------------------------------------------------------- messages

  listConversations(): Observable<readonly ConversationSummary[]> {
    return this.http.get<readonly ConversationSummary[]>(`${this.root}/conversations`);
  }

  startConversation(handle: string): Observable<ConversationDetail> {
    return this.http.post<ConversationDetail>(`${this.root}/conversations`, { handle });
  }

  getConversation(conversationId: string, page = 1): Observable<ConversationDetail> {
    return this.http.get<ConversationDetail>(`${this.root}/conversations/${conversationId}`, {
      params: new HttpParams().set('page', page),
    });
  }

  sendMessage(conversationId: string, body: string): Observable<ConversationDetail> {
    return this.http.post<ConversationDetail>(
      `${this.root}/conversations/${conversationId}/messages`,
      { body },
    );
  }

  deleteMessage(messageId: string): Observable<ConversationDetail> {
    return this.http.delete<ConversationDetail>(`${this.root}/messages/${messageId}`);
  }

  // ---------------------------------------------------------------- notifications

  listNotifications(page = 1): Observable<PagedResult<NotificationView>> {
    return this.http.get<PagedResult<NotificationView>>(`${this.base}/v1/me/notifications`, {
      params: new HttpParams().set('page', page),
    });
  }

  markNotificationsRead(notificationId: string | null): Observable<void> {
    return this.http.put<void>(`${this.base}/v1/me/notifications/read`, { notificationId });
  }

  // ---------------------------------------------------------------- moderation

  getOverview(): Observable<AdminOverview> {
    return this.http.get<AdminOverview>(`${this.base}/v1/admin/community/overview`);
  }

  listAdminCategories(): Observable<readonly AdminForumCategory[]> {
    return this.http.get<readonly AdminForumCategory[]>(
      `${this.base}/v1/admin/community/categories`,
    );
  }

  createCategory(
    slug: string,
    name: string,
    description: string,
    sortOrder: number,
  ): Observable<AdminForumCategory> {
    return this.http.post<AdminForumCategory>(`${this.base}/v1/admin/community/categories`, {
      slug,
      name,
      description,
      sortOrder,
    });
  }

  setCategoryStatus(
    categoryId: string,
    targetStatus: 'Active' | 'Archived',
    reason: string,
    rowVersion: string,
  ): Observable<AdminForumCategory> {
    return this.http.post<AdminForumCategory>(
      `${this.base}/v1/admin/community/categories/${categoryId}/status/${targetStatus}`,
      { reason, rowVersion },
    );
  }

  listReports(status: string, page = 1): Observable<PagedResult<ModerationReport>> {
    let params = new HttpParams().set('page', page);

    if (status) {
      params = params.set('status', status);
    }

    return this.http.get<PagedResult<ModerationReport>>(`${this.base}/v1/admin/community/reports`, {
      params,
    });
  }

  getReportTarget(reportId: string): Observable<ModerationTarget> {
    return this.http.get<ModerationTarget>(
      `${this.base}/v1/admin/community/reports/${reportId}/target`,
    );
  }

  decideReport(
    reportId: string,
    targetStatus: string,
    reason: string,
    rowVersion: string,
  ): Observable<ModerationReport> {
    return this.http.post<ModerationReport>(
      `${this.base}/v1/admin/community/reports/${reportId}/status/${targetStatus}`,
      { reason, rowVersion },
    );
  }

  removePostAsModerator(postId: string, reason: string): Observable<void> {
    return this.http.post<void>(`${this.base}/v1/admin/community/posts/${postId}/remove`, {
      reason,
    });
  }

  setThreadStatus(threadId: string, targetStatus: string, reason: string): Observable<void> {
    return this.http.post<void>(
      `${this.base}/v1/admin/community/threads/${threadId}/status/${targetStatus}`,
      { reason },
    );
  }

  setProfileStatus(userId: string, targetStatus: string, reason: string): Observable<void> {
    return this.http.post<void>(
      `${this.base}/v1/admin/community/profiles/${userId}/status/${targetStatus}`,
      { reason },
    );
  }
}
