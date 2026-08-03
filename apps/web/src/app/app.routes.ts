import { Routes } from '@angular/router';

import { adminGuard, authenticatedGuard } from './core/auth/admin.guard';
import { Account } from './features/account/account';
import { Admin } from './features/admin/admin';
import { Home } from './features/home/home';

export const routes: Routes = [
  {
    path: '',
    component: Home,
    title: "Daniel's Dojo",
  },
  {
    path: 'account',
    component: Account,
    title: "Your account — Daniel's Dojo",
  },
  {
    // Guards here are user experience only. The API authorizes every request again against
    // the local database, so a bypassed guard grants nothing.
    path: 'admin',
    component: Admin,
    canActivate: [authenticatedGuard, adminGuard],
    title: "Administration — Daniel's Dojo",
  },
  {
    path: '**',
    redirectTo: '',
  },
];
