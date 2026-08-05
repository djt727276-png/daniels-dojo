import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';

import { toApiFailure } from '../../../core/api/problem-details';
import {
  AdminAuditEntryView,
  AdminCertificateView,
  AdminOperationsApi,
  AdminOrderView,
  AdminWebhookEventView,
} from '../../../core/admin/admin-operations-api';
import {
  ConfirmDialog,
  ConfirmDialogResult,
} from '../../../shared/ui/confirm-dialog/confirm-dialog';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { EmptyState } from '../../../shared/ui/state-views/state-views';

/**
 * The platform's records: certificates, orders, provider events, and the audit trail.
 *
 * Read-only apart from certificate revocation, which requires a recorded reason. Nothing
 * here shows message bodies or personal content beyond what the records themselves carry.
 */
@Component({
  selector: 'app-admin-records',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatButtonModule,
    MatTabsModule,
    MatFormFieldModule,
    MatInputModule,
    PageHeader,
    EmptyState,
  ],
  template: `
    <div class="dd-page dd-stack">
      <app-page-header
        title="Records"
        description="Certificates, orders, payment events, and the audit trail — read from the live database."
      >
        <a matButton routerLink="/admin">Back to administration</a>
      </app-page-header>

      @if (failure(); as message) {
        <p class="records__failure" role="alert">{{ message }}</p>
      }

      <mat-tab-group>
        <mat-tab label="Certificates">
          <div class="records__pane dd-stack">
            <mat-form-field appearance="outline" class="records__search">
              <mat-label>Search holder, course, or code</mat-label>
              <input
                matInput
                type="search"
                [formControl]="certificateSearch"
                data-testid="certificate-search"
              />
            </mat-form-field>

            @if (certificates().length === 0) {
              <app-empty-state
                title="No certificates"
                message="Certificates appear as members complete courses."
              />
            } @else {
              <div class="records__scroll">
                <table class="records__table" data-testid="certificate-table">
                  <thead>
                    <tr>
                      <th scope="col">Holder</th>
                      <th scope="col">Course</th>
                      <th scope="col">Code</th>
                      <th scope="col">Issued</th>
                      <th scope="col">Status</th>
                      <th scope="col"><span class="cdk-visually-hidden">Actions</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (certificate of certificates(); track certificate.id) {
                      <tr>
                        <td>{{ certificate.holderName }}</td>
                        <td>{{ certificate.courseTitle }}</td>
                        <td class="records__code">{{ certificate.verificationCode }}</td>
                        <td>{{ certificate.issuedAtUtc | date: 'mediumDate' }}</td>
                        <td>
                          {{ certificate.revokedAtUtc ? 'Revoked' : 'Valid' }}
                        </td>
                        <td>
                          @if (!certificate.revokedAtUtc) {
                            <button
                              matButton
                              type="button"
                              [disabled]="busy()"
                              (click)="revoke(certificate)"
                              [attr.data-testid]="'revoke-' + certificate.id"
                            >
                              Revoke
                            </button>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        </mat-tab>

        <mat-tab label="Orders">
          <div class="records__pane dd-stack">
            @if (orders().length === 0) {
              <app-empty-state title="No orders" message="Orders appear as customers buy." />
            } @else {
              <div class="records__scroll">
                <table class="records__table" data-testid="order-table">
                  <thead>
                    <tr>
                      <th scope="col">Customer</th>
                      <th scope="col">What</th>
                      <th scope="col">Total</th>
                      <th scope="col">Status</th>
                      <th scope="col">Created</th>
                      <th scope="col">Paid</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (order of orders(); track order.id) {
                      <tr>
                        <td>{{ order.customerEmail }}</td>
                        <td>{{ order.offerName }}</td>
                        <td>{{ money(order.totalMinor, order.currency) }}</td>
                        <td>{{ order.status }}</td>
                        <td>{{ order.createdAtUtc | date: 'short' }}</td>
                        <td>{{ order.paidAtUtc ? (order.paidAtUtc | date: 'short') : '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        </mat-tab>

        <mat-tab label="Payment events">
          <div class="records__pane dd-stack">
            @if (webhookEvents().length === 0) {
              <app-empty-state
                title="No events"
                message="Provider webhook deliveries appear here as they arrive."
              />
            } @else {
              <div class="records__scroll">
                <table class="records__table" data-testid="webhook-table">
                  <thead>
                    <tr>
                      <th scope="col">Provider</th>
                      <th scope="col">Event</th>
                      <th scope="col">Status</th>
                      <th scope="col">Received</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (entry of webhookEvents(); track entry.id) {
                      <tr>
                        <td>{{ entry.provider }}</td>
                        <td class="records__code">{{ entry.eventType }}</td>
                        <td>{{ entry.status }}</td>
                        <td>{{ entry.receivedAtUtc | date: 'short' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        </mat-tab>

        <mat-tab label="Audit trail">
          <div class="records__pane dd-stack">
            <mat-form-field appearance="outline" class="records__search">
              <mat-label>Filter by action prefix (e.g. Commerce.)</mat-label>
              <input
                matInput
                type="search"
                [formControl]="auditFilter"
                data-testid="audit-filter"
              />
            </mat-form-field>

            @if (audit().length === 0) {
              <app-empty-state
                title="Nothing recorded"
                message="Privileged actions appear here as they happen."
              />
            } @else {
              <ol class="records__audit" data-testid="audit-list">
                @for (entry of audit(); track entry.id) {
                  <li class="records__audit-item">
                    <p>
                      <strong>{{ entry.action }}</strong> — {{ entry.targetType }}
                      <span class="records__code">{{ entry.targetId }}</span>
                    </p>
                    <p class="records__audit-meta">
                      {{ entry.actorName }} · {{ entry.occurredAtUtc | date: 'short' }}
                      @if (entry.reason) {
                        · “{{ entry.reason }}”
                      }
                    </p>
                    @if (entry.metadataJson) {
                      <pre class="records__metadata">{{ entry.metadataJson }}</pre>
                    }
                  </li>
                }
              </ol>
            }
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: `
    .records__pane {
      padding-top: var(--dd-space-4);
    }

    .records__search {
      max-width: 24rem;
    }

    .records__failure {
      padding: var(--dd-space-3) var(--dd-space-4);
      color: var(--dd-danger);
      background: var(--dd-danger-container);
      border-radius: var(--dd-radius-md);
    }

    .records__scroll {
      overflow-x: auto;
    }

    .records__table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--dd-text-sm);

      th,
      td {
        padding: var(--dd-space-2) var(--dd-space-3);
        text-align: left;
        border-bottom: 1px solid var(--dd-outline);
        white-space: nowrap;
      }

      th {
        font-weight: var(--dd-weight-medium);
        color: var(--dd-on-surface-variant);
      }
    }

    .records__code {
      font-family: var(--dd-font-mono, monospace);
      font-size: var(--dd-text-sm);
      overflow-wrap: anywhere;
    }

    .records__audit {
      display: flex;
      flex-direction: column;
      gap: var(--dd-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .records__audit-item {
      padding: var(--dd-space-3);
      border: 1px solid var(--dd-outline);
      border-radius: var(--dd-radius-md);
    }

    .records__audit-meta {
      font-size: var(--dd-text-sm);
      color: var(--dd-on-surface-variant);
    }

    .records__metadata {
      margin-top: var(--dd-space-2);
      padding: var(--dd-space-2);
      font-size: var(--dd-text-sm);
      background: var(--dd-surface-variant);
      border-radius: var(--dd-radius-sm);
      overflow-x: auto;
    }
  `,
})
export class AdminRecords {
  private readonly api = inject(AdminOperationsApi);
  private readonly dialog = inject(MatDialog);

  protected readonly certificates = signal<readonly AdminCertificateView[]>([]);
  protected readonly orders = signal<readonly AdminOrderView[]>([]);
  protected readonly webhookEvents = signal<readonly AdminWebhookEventView[]>([]);
  protected readonly audit = signal<readonly AdminAuditEntryView[]>([]);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly certificateSearch = new FormControl('', { nonNullable: true });
  protected readonly auditFilter = new FormControl('', { nonNullable: true });

  constructor() {
    this.loadCertificates('');
    this.loadAudit('');

    this.api.listOrders().subscribe({
      next: (page) => this.orders.set(page.items),
      error: (error: unknown) => this.fail(error),
    });
    this.api.listWebhookEvents().subscribe({
      next: (page) => this.webhookEvents.set(page.items),
      error: (error: unknown) => this.fail(error),
    });

    this.certificateSearch.valueChanges
      .pipe(debounceTime(300))
      .subscribe((term) => this.loadCertificates(term));
    this.auditFilter.valueChanges.pipe(debounceTime(300)).subscribe((term) => this.loadAudit(term));
  }

  protected money(totalMinor: number, currency: string): string {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(
      totalMinor / 100,
    );
  }

  protected revoke(certificate: AdminCertificateView): void {
    this.dialog
      .open<ConfirmDialog, unknown, ConfirmDialogResult>(ConfirmDialog, {
        data: {
          title: `Revoke ${certificate.holderName}'s certificate?`,
          message: 'The public verification page will report it revoked. The reason is recorded.',
          confirmLabel: 'Revoke',
          destructive: true,
          requireReason: true,
          reasonLabel: 'Reason (recorded)',
        },
        width: '32rem',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }

        this.busy.set(true);
        this.api.revokeCertificate(certificate.id, result.reason).subscribe({
          next: () => {
            this.busy.set(false);
            this.loadCertificates(this.certificateSearch.value);
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.fail(error);
          },
        });
      });
  }

  private loadCertificates(term: string): void {
    this.api.listCertificates(term.trim()).subscribe({
      next: (page) => this.certificates.set(page.items),
      error: (error: unknown) => this.fail(error),
    });
  }

  private loadAudit(term: string): void {
    this.api.listAudit(term.trim()).subscribe({
      next: (page) => this.audit.set(page.items),
      error: (error: unknown) => this.fail(error),
    });
  }

  private fail(error: unknown): void {
    this.failure.set(toApiFailure(error, 'Records could not be loaded.').message);
  }
}
