import { MatDialog } from '@angular/material/dialog';
import { Observable } from 'rxjs';

import { PublicationStatus } from '../../../core/admin/admin-catalog.model';
import {
  ConfirmDialog,
  ConfirmDialogResult,
} from '../../../shared/ui/confirm-dialog/confirm-dialog';

/** What a status command will do, in the operator's words. */
function describe(entity: string, target: PublicationStatus): { title: string; message: string } {
  switch (target) {
    case 'Published':
      return {
        title: `Publish this ${entity}?`,
        message: `Publishing makes this ${entity} visible to students. Publishing does not cascade — sections and lessons keep their own status.`,
      };
    case 'Draft':
      return {
        title: `Return this ${entity} to draft?`,
        message: `Students will no longer see this ${entity}. Existing purchases are unaffected.`,
      };
    case 'Archived':
      return {
        title: `Archive this ${entity}?`,
        message: `Archiving withdraws this ${entity} from the catalog. An archived record can only return to draft, not straight back to published.`,
      };
  }
}

/**
 * Asks for confirmation and a reason before a status change.
 *
 * The API requires a non-blank reason for every status change and records it in the audit
 * trail, so the dialog collects one rather than letting the request fail after the fact.
 */
export function confirmStatusChange(
  dialog: MatDialog,
  entity: string,
  target: PublicationStatus,
): Observable<ConfirmDialogResult | undefined> {
  const { title, message } = describe(entity, target);

  return dialog
    .open(ConfirmDialog, {
      data: {
        title,
        message,
        confirmLabel: target === 'Published' ? 'Publish' : `Move to ${target.toLowerCase()}`,
        destructive: target === 'Archived',
        requireReason: true,
        reasonLabel: 'Reason (recorded in the audit trail)',
      },
      width: '32rem',
    })
    .afterClosed();
}

/** Verb shown on the button that performs a transition. */
export function transitionLabel(target: PublicationStatus): string {
  switch (target) {
    case 'Published':
      return 'Publish';
    case 'Draft':
      return 'Return to draft';
    case 'Archived':
      return 'Archive';
  }
}
