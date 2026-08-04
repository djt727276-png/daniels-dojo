import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { DevAuthBanner } from '../dev-auth-banner/dev-auth-banner';

/** One entry in the primary navigation. */
interface NavItem {
  readonly label: string;
  readonly route: string;
  readonly testId: string;
}

/**
 * Application shell: skip link, sticky toolbar, responsive navigation, account
 * menu, and the routed main landmark.
 *
 * The sidenav is permanent from the medium breakpoint upward and becomes an
 * overlay drawer below it, so the layout stays usable down to 320px without a
 * separate mobile template.
 *
 * Navigation visibility is driven by the roles the API returned for the
 * session. It is presentation only — every protected route and endpoint is
 * authorized again on the server.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatToolbarModule,
    MatSidenavModule,
    MatButtonModule,
    MatMenuModule,
    MatDividerModule,
    DevAuthBanner,
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
  private readonly auth = inject(AuthService);
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly productName = "Daniel's Dojo";

  protected readonly session = this.auth.session;
  protected readonly isAdmin = this.auth.isAdmin;

  /** True once the viewport is wide enough for a permanent sidenav. */
  protected readonly isWide = toSignal(
    this.breakpoints
      .observe([Breakpoints.Medium, Breakpoints.Large, Breakpoints.XLarge])
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  protected readonly drawerOpen = signal(false);

  /** Always available, signed in or not. */
  protected readonly publicNav: readonly NavItem[] = [
    { label: 'Home', route: '/', testId: 'nav-home' },
    { label: 'Courses', route: '/courses', testId: 'nav-courses' },
  ];

  /** Shown once the API reports a session. */
  protected readonly memberNav: readonly NavItem[] = [
    { label: 'Dashboard', route: '/dashboard', testId: 'nav-dashboard' },
    { label: 'My Learning', route: '/my-learning', testId: 'nav-my-learning' },
    { label: 'Community', route: '/community', testId: 'nav-community' },
    { label: 'People', route: '/people', testId: 'nav-people' },
    { label: 'Friends', route: '/friends', testId: 'nav-friends' },
    { label: 'Messages', route: '/messages', testId: 'nav-messages' },
    { label: 'Notifications', route: '/notifications', testId: 'nav-notifications' },
  ];

  /** Shown only when the API reported the Admin role. */
  protected readonly adminNav: readonly NavItem[] = [
    { label: 'Admin', route: '/admin', testId: 'nav-admin' },
    { label: 'Catalog', route: '/admin/catalog', testId: 'nav-admin-catalog' },
    { label: 'Moderation', route: '/admin/community', testId: 'nav-admin-community' },
  ];

  protected toggleDrawer(): void {
    this.drawerOpen.update((open) => !open);
  }

  /** Closes the overlay drawer after navigation on small viewports. */
  protected onNavigate(): void {
    if (!this.isWide()) {
      this.drawerOpen.set(false);
    }
  }

  protected signOut(): void {
    this.auth.signOut();
  }
}
