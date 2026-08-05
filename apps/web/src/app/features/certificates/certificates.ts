import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, Injectable, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { toApiFailure } from '../../core/api/problem-details';
import { API_BASE_PATH } from '../../core/configuration/app-config';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { EmptyState, ErrorState, LoadingState } from '../../shared/ui/state-views/state-views';

/** One certificate as its holder sees it. */
export interface CertificateView {
  readonly id: string;
  readonly courseId: string;
  readonly courseTitle: string;
  readonly holderName: string;
  readonly verificationCode: string;
  readonly issuedAtUtc: string;
  readonly isValid: boolean;
}

/** What a verification code discloses: the certificate face, nothing else. */
export interface CertificateVerification {
  readonly courseTitle: string;
  readonly holderName: string;
  readonly issuedAtUtc: string;
  readonly isValid: boolean;
  readonly revokedAtUtc: string | null;
}

@Injectable({ providedIn: 'root' })
export class CertificatesApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_PATH);

  listMine(): Observable<readonly CertificateView[]> {
    return this.http.get<readonly CertificateView[]>(`${this.base}/v1/learning/certificates`);
  }

  verify(code: string): Observable<CertificateVerification> {
    return this.http.get<CertificateVerification>(
      `${this.base}/v1/certificates/${encodeURIComponent(code)}/verify`,
    );
  }
}

/**
 * The member's earned certificates, each printable.
 *
 * "Download" is the browser's own print-to-PDF of the certificate view below — an honest
 * document produced from real completion data, not a generated image pretending to be
 * parchment.
 */
@Component({
  selector: 'app-my-certificates',
  imports: [
    RouterLink,
    DatePipe,
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
        title="Certificates"
        description="Earned by completing every lesson of a course. Each one verifies publicly by its code."
      />

      @switch (state().kind) {
        @case ('loading') {
          <app-loading-state message="Loading your certificates…" />
        }
        @case ('error') {
          <app-error-state [message]="message()" (retry)="load()" />
        }
        @default {
          @if (certificates().length === 0) {
            <app-empty-state
              title="No certificates yet"
              message="Finish every lesson of a course and its certificate appears here."
            >
              <a matButton="filled" routerLink="/my-learning">Go to My Learning</a>
            </app-empty-state>
          } @else {
            <ul class="certs">
              @for (cert of certificates(); track cert.id) {
                <li>
                  <mat-card appearance="outlined" [class.certs__revoked]="!cert.isValid">
                    <mat-card-content class="certs__body">
                      <div>
                        <h2 class="certs__title">{{ cert.courseTitle }}</h2>
                        <p class="certs__meta">
                          Issued {{ cert.issuedAtUtc | date: 'longDate' }} · Code
                          <code>{{ cert.verificationCode }}</code>
                          @if (!cert.isValid) {
                            · <strong>Revoked</strong>
                          }
                        </p>
                      </div>
                      <div class="certs__actions">
                        <a matButton="filled" [routerLink]="['/verify', cert.verificationCode]">
                          View & print
                        </a>
                      </div>
                    </mat-card-content>
                  </mat-card>
                </li>
              }
            </ul>
          }
        }
      }
    </div>
  `,
  styles: `
    .certs {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .certs__body {
      display: flex;
      flex-wrap: wrap;
      gap: var(--dd-space-4);
      align-items: center;
      justify-content: space-between;
    }

    .certs__title {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-medium);
    }

    .certs__meta {
      color: var(--dd-on-surface-variant);
      overflow-wrap: anywhere;
    }

    .certs__revoked {
      opacity: 0.7;
    }
  `,
})
export class MyCertificates {
  private readonly api = inject(CertificatesApi);

  protected readonly state = signal<
    | { kind: 'loading' }
    | { kind: 'ready'; certificates: readonly CertificateView[] }
    | { kind: 'error'; message: string }
  >({ kind: 'loading' });

  protected readonly certificates = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.certificates : [];
  });

  protected readonly message = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.api.listMine().subscribe({
      next: (certificates) => this.state.set({ kind: 'ready', certificates }),
      error: (error: unknown) =>
        this.state.set({ kind: 'error', message: toApiFailure(error).message }),
    });
  }
}

/**
 * Public verification of one certificate code.
 *
 * Anyone holding a code — an employer, a colleague — lands here and sees exactly what the
 * certificate face says, plus whether it still stands. A revoked certificate says revoked;
 * an unknown code says unknown. Print styles make this page the certificate document itself.
 */
@Component({
  selector: 'app-certificate-verify',
  imports: [DatePipe, MatButtonModule, LoadingState, ErrorState],
  template: `
    @switch (state().kind) {
      @case ('loading') {
        <app-loading-state message="Checking the certificate…" />
      }
      @case ('error') {
        <app-error-state title="Certificate not found" [message]="message()" retryLabel="" />
      }
      @default {
        @if (verification(); as cert) {
          <div class="verify" [class.verify--revoked]="!cert.isValid">
            <div class="verify__card" role="document">
              <p class="verify__brand">Daniel's Dojo</p>
              <p class="verify__label">Certificate of completion</p>
              <h1 class="verify__holder">{{ cert.holderName }}</h1>
              <p class="verify__completed">completed</p>
              <h2 class="verify__course">{{ cert.courseTitle }}</h2>
              <p class="verify__date">{{ cert.issuedAtUtc | date: 'longDate' }}</p>
              <p class="verify__code">Verification code: {{ code() }}</p>

              @if (cert.isValid) {
                <p class="verify__status verify__status--valid" role="status">
                  ✓ Verified — this certificate is valid.
                </p>
              } @else {
                <p class="verify__status verify__status--revoked" role="alert">
                  This certificate was revoked on {{ cert.revokedAtUtc | date: 'longDate' }} and is
                  no longer valid.
                </p>
              }
            </div>

            <div class="verify__actions">
              <button matButton="filled" type="button" (click)="print()">
                Print or save as PDF
              </button>
            </div>
          </div>
        }
      }
    }
  `,
  styles: `
    .verify {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-5);
      align-items: center;
      padding: var(--dd-space-6) var(--dd-space-4);
    }

    .verify__card {
      width: 100%;
      max-width: 44rem;
      padding: var(--dd-space-10) var(--dd-space-6);
      text-align: center;
      background: var(--dd-surface);
      border: 3px double var(--dd-accent);
      border-radius: var(--dd-radius-lg);
    }

    .verify__brand {
      font-size: var(--dd-text-lg);
      font-weight: var(--dd-weight-bold);
      color: var(--dd-primary);
    }

    .verify__label {
      margin-top: var(--dd-space-2);
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
      text-transform: uppercase;
      letter-spacing: 0.12em;
    }

    .verify__holder {
      margin-top: var(--dd-space-6);
      font-size: var(--dd-text-3xl);
      font-weight: var(--dd-weight-bold);
    }

    .verify__completed {
      margin-top: var(--dd-space-3);
      color: var(--dd-on-surface-variant);
    }

    .verify__course {
      margin-top: var(--dd-space-2);
      font-size: var(--dd-text-xl);
      font-weight: var(--dd-weight-medium);
      color: var(--dd-primary);
    }

    .verify__date {
      margin-top: var(--dd-space-4);
      color: var(--dd-on-surface-variant);
    }

    .verify__code {
      margin-top: var(--dd-space-6);
      font-family: var(--dd-font-mono);
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
      overflow-wrap: anywhere;
    }

    .verify__status {
      margin-top: var(--dd-space-5);
      font-weight: var(--dd-weight-medium);
    }

    .verify__status--valid {
      color: var(--dd-success);
    }

    .verify__status--revoked {
      color: var(--dd-danger);
    }

    .verify--revoked .verify__card {
      border-color: var(--dd-danger);
    }

    @media print {
      .verify__actions {
        display: none;
      }

      .verify__card {
        border-width: 4px;
      }
    }
  `,
})
export class CertificateVerify {
  private readonly api = inject(CertificatesApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly code = signal(this.route.snapshot.paramMap.get('code') ?? '');

  protected readonly state = signal<
    | { kind: 'loading' }
    | { kind: 'ready'; verification: CertificateVerification }
    | { kind: 'error'; message: string }
  >({ kind: 'loading' });

  protected readonly verification = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.verification : null;
  });

  protected readonly message = computed(() => {
    const current = this.state();
    return current.kind === 'error' ? current.message : '';
  });

  constructor() {
    this.api.verify(this.code()).subscribe({
      next: (verification) => this.state.set({ kind: 'ready', verification }),
      error: () =>
        this.state.set({
          kind: 'error',
          message:
            'No certificate matches this code. Check the code on the document and try again.',
        }),
    });
  }

  protected print(): void {
    window.print();
  }
}
