import { Injectable, NgZone, OnDestroy, inject } from '@angular/core';
import { MsalService } from '@azure/msal-angular';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';

import { AUTH_CONFIG, isDevelopmentAuthAllowed } from '../configuration/auth-config';
import { DevelopmentAuthClient } from '../auth/development-auth';

/**
 * The live community channel: a doorbell, not a data source.
 *
 * The server never pushes content over this connection — it pushes "something changed, go
 * and fetch", and every screen that listens re-reads the same REST endpoints it already
 * trusts. That keeps the database the single source of truth: a member who was offline,
 * whose socket dropped, or whose browser slept reconciles by fetching, and nothing exists
 * only in flight.
 *
 * The connection is started lazily by the first listener and shared by all of them. On
 * automatic reconnect the service emits {@link reconnected} so listeners refetch whatever
 * they display — the events missed while disconnected are already in the database.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService implements OnDestroy {
  private readonly config = inject(AUTH_CONFIG);
  private readonly msal = inject(MsalService);
  private readonly developmentAuth = inject(DevelopmentAuthClient);
  private readonly zone = inject(NgZone);

  private connection: HubConnection | null = null;

  private readonly messageReceivedSubject = new Subject<string>();
  private readonly unreadChangedSubject = new Subject<void>();
  private readonly reconnectedSubject = new Subject<void>();

  /** A direct message arrived in this conversation; refetch it. */
  readonly messageReceived: Observable<string> = this.messageReceivedSubject.asObservable();

  /** Unread counters changed; refetch them. */
  readonly unreadChanged: Observable<void> = this.unreadChangedSubject.asObservable();

  /** The connection came back after a drop; refetch whatever is on screen. */
  readonly reconnected: Observable<void> = this.reconnectedSubject.asObservable();

  /**
   * Ensures the shared connection is up. Safe to call repeatedly; failures are silent
   * because the doorbell is an enhancement — REST still works without it.
   */
  connect(): void {
    if (this.connection !== null) {
      if (this.connection.state === HubConnectionState.Disconnected) {
        this.connection.start().catch(() => undefined);
      }

      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl(this.hubUrl(), { accessTokenFactory: () => this.token() })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();

    // Handlers run outside Angular's zone; re-enter it so change detection sees updates.
    connection.on('MessageReceived', (conversationId: string) =>
      this.zone.run(() => this.messageReceivedSubject.next(conversationId)),
    );
    connection.on('UnreadChanged', () => this.zone.run(() => this.unreadChangedSubject.next()));
    connection.onreconnected(() => this.zone.run(() => this.reconnectedSubject.next()));

    this.connection = connection;
    connection.start().catch(() => undefined);
  }

  ngOnDestroy(): void {
    this.connection?.stop().catch(() => undefined);
    this.connection = null;
  }

  /**
   * The hub lives at the API origin under `/hubs`, beside — not under — the `/api` base
   * path. Locally the relative form rides the dev-server proxy; hosted, the configured
   * absolute API base supplies the origin.
   */
  private hubUrl(): string {
    const base = this.config.apiBaseUrl;

    if (base.startsWith('/')) {
      return '/hubs/community';
    }

    try {
      return `${new URL(base).origin}/hubs/community`;
    } catch {
      return '/hubs/community';
    }
  }

  /**
   * The same token the HTTP interceptor would attach, from whichever mode is active.
   * SignalR sends it as an `access_token` query parameter, which the API accepts for
   * `/hubs` paths only.
   */
  private async token(): Promise<string> {
    if (isDevelopmentAuthAllowed(this.config)) {
      return this.developmentAuth.read() ?? '';
    }

    const account = this.msal.instance.getAllAccounts()[0];

    if (!account) {
      return '';
    }

    try {
      const result = await this.msal.instance.acquireTokenSilent({
        scopes: [this.config.apiScope],
        account,
      });

      return result.accessToken;
    } catch {
      return '';
    }
  }
}
