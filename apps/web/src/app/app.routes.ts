import { Routes } from '@angular/router';

import { adminGuard, authenticatedGuard } from './core/auth/admin.guard';

/**
 * Every routed screen is lazy-loaded.
 *
 * The initial bundle then carries only the shell — toolbar, sidenav, navigation, and the
 * auth session — so adding a feature adds a chunk rather than weight to first load. Guards
 * are user experience only; the API authorizes every request again against the database.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
    title: "Daniel's Dojo",
  },
  {
    path: 'courses',
    loadComponent: () => import('./features/catalog/course-list').then((m) => m.CourseList),
    title: "Courses — Daniel's Dojo",
  },
  {
    // Declared before the ':slug' route so the more specific preview path wins.
    path: 'courses/:courseSlug/preview/:lessonSlug',
    loadComponent: () => import('./features/catalog/lesson-preview').then((m) => m.LessonPreview),
    title: "Free preview — Daniel's Dojo",
  },
  {
    path: 'courses/:slug',
    loadComponent: () => import('./features/catalog/course-detail').then((m) => m.CourseDetail),
    title: "Course — Daniel's Dojo",
  },
  {
    path: 'account',
    loadComponent: () => import('./features/account/account').then((m) => m.Account),
    title: "Your account — Daniel's Dojo",
  },
  {
    // Development sign-in. The page reports that it is unavailable in a production bundle, and
    // the API endpoint it calls does not exist outside Development.
    path: 'development-login',
    loadComponent: () =>
      import('./features/development-login/development-login').then((m) => m.DevelopmentLogin),
    title: "Development sign-in — Daniel's Dojo",
  },
  {
    path: 'admin',
    loadComponent: () => import('./features/admin/admin').then((m) => m.Admin),
    canActivate: [authenticatedGuard, adminGuard],
    title: "Administration — Daniel's Dojo",
  },
  {
    path: '**',
    redirectTo: '',
  },
];
