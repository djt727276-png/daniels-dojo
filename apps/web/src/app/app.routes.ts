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
    path: 'dashboard',
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
    canActivate: [authenticatedGuard],
    title: "Dashboard — Daniel's Dojo",
  },
  {
    path: 'my-learning',
    loadComponent: () => import('./features/my-learning/my-learning').then((m) => m.MyLearning),
    canActivate: [authenticatedGuard],
    title: "My Learning — Daniel's Dojo",
  },
  {
    // Declared before ':categorySlug' style routes so setup is never read as a category.
    path: 'community/setup',
    loadComponent: () =>
      import('./features/community/community-setup').then((m) => m.CommunitySetup),
    canActivate: [authenticatedGuard],
    title: "Community setup — Daniel's Dojo",
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
    path: 'admin/catalog',
    loadComponent: () =>
      import('./features/admin/catalog/admin-course-list').then((m) => m.AdminCourseList),
    canActivate: [authenticatedGuard, adminGuard],
    title: "Catalog — Daniel's Dojo",
  },
  {
    // Declared before ':courseId' so "new" is never treated as an identifier.
    path: 'admin/catalog/courses/new',
    loadComponent: () =>
      import('./features/admin/catalog/admin-course-create').then((m) => m.AdminCourseCreate),
    canActivate: [authenticatedGuard, adminGuard],
    title: "New course — Daniel's Dojo",
  },
  {
    path: 'admin/catalog/courses/:courseId',
    loadComponent: () =>
      import('./features/admin/catalog/admin-course-workspace').then((m) => m.AdminCourseWorkspace),
    canActivate: [authenticatedGuard, adminGuard],
    title: "Course workspace — Daniel's Dojo",
  },
  {
    path: 'admin/pricing',
    loadComponent: () =>
      import('./features/admin/pricing/admin-pricing').then((m) => m.AdminPricing),
    canActivate: [authenticatedGuard, adminGuard],
    title: "Pricing — Daniel's Dojo",
  },
  {
    path: 'community',
    loadComponent: () => import('./features/community/community-home').then((m) => m.CommunityHome),
    canActivate: [authenticatedGuard],
    title: "Community — Daniel's Dojo",
  },
  {
    path: 'community/c/:categorySlug',
    loadComponent: () =>
      import('./features/community/category-threads').then((m) => m.CategoryThreads),
    canActivate: [authenticatedGuard],
    title: "Category — Daniel's Dojo",
  },
  {
    path: 'community/t/:threadId',
    loadComponent: () => import('./features/community/thread-detail').then((m) => m.ThreadDetail),
    canActivate: [authenticatedGuard],
    title: "Thread — Daniel's Dojo",
  },
  {
    path: 'people',
    loadComponent: () => import('./features/community/people').then((m) => m.People),
    canActivate: [authenticatedGuard],
    title: "People — Daniel's Dojo",
  },
  {
    path: 'friends',
    loadComponent: () => import('./features/community/friends').then((m) => m.Friends),
    canActivate: [authenticatedGuard],
    title: "Friends — Daniel's Dojo",
  },
  {
    path: 'messages',
    loadComponent: () => import('./features/community/messages').then((m) => m.MessageList),
    canActivate: [authenticatedGuard],
    title: "Messages — Daniel's Dojo",
  },
  {
    path: 'messages/:conversationId',
    loadComponent: () => import('./features/community/messages').then((m) => m.Conversation),
    canActivate: [authenticatedGuard],
    title: "Conversation — Daniel's Dojo",
  },
  {
    path: 'notifications',
    loadComponent: () => import('./features/community/notifications').then((m) => m.Notifications),
    canActivate: [authenticatedGuard],
    title: "Notifications — Daniel's Dojo",
  },
  {
    path: 'admin/community',
    loadComponent: () =>
      import('./features/admin/moderation/admin-moderation').then((m) => m.AdminModeration),
    canActivate: [authenticatedGuard, adminGuard],
    title: "Moderation — Daniel's Dojo",
  },
  {
    path: '**',
    redirectTo: '',
  },
];
