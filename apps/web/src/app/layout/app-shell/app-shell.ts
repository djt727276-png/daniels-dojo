import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { UnreadStatusService } from '../../core/community/unread-status.service';
import { ThemeService } from '../../core/configuration/theme.service';
import { DdLogo } from '../../shared/ui/brand/dd-logo';
import { DdIcon, DdIconName } from '../../shared/ui/icon/dd-icon';
import { AppFooter } from '../app-footer/app-footer';
import { DevAuthBanner } from '../dev-auth-banner/dev-auth-banner';

/** One entry in the primary navigation. */
interface NavItem {
  readonly label: string;
  readonly route: string;
  readonly icon: DdIconName;
  readonly testId: string;
}

/**
 * Application shell: skip link, glass top bar, the left dojo rail, the
 * mobile bottom navigation, and the routed main landmark.
 *
 * The rail is permanent from the medium breakpoint upward and becomes an
 * overlay drawer below it; under the small breakpoint a bottom navigation
 * carries the five primary destinations, so the layout stays usable down to
 * 320px without a separate mobile template.
 *
 * Navigation visibility is driven by the roles the API returned for the
 * session. It is presentation only — every protected route and endpoint is
 * authorized again on the server. Unread badges are real counts from the
 * member dashboard endpoint, refreshed over the realtime doorbell.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatToolbarModule,
    MatTooltipModule,
    MatSidenavModule,
    MatButtonModule,
    MatMenuModule,
    MatDividerModule,
    DevAuthBanner,
    AppFooter,
    DdLogo,
    DdIcon,
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
  private readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly unread = inject(UnreadStatusService);
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly productName = "Daniel's Dojo";

  protected readonly session = this.auth.session;
  protected readonly isAdmin = this.auth.isAdmin;

  /** True once the viewport is wide enough for a permanent rail. */
  protected readonly isWide = toSignal(
    this.breakpoints
      .observe([Breakpoints.Medium, Breakpoints.Large, Breakpoints.XLarge])
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  protected readonly drawerOpen = signal(false);

  /** Always available, signed in or not. */
  protected readonly publicNav: readonly NavItem[] = [
    { label: 'Home', route: '/', icon: 'home', testId: 'nav-home' },
    { label: 'Courses', route: '/courses', icon: 'book', testId: 'nav-courses' },
    { label: 'Pricing', route: '/pricing', icon: 'tag', testId: 'nav-pricing' },
  ];

  /** Shown once the API reports a session. */
  protected readonly memberNav: readonly NavItem[] = [
    { label: 'Dashboard', route: '/dashboard', icon: 'compass', testId: 'nav-dashboard' },
    { label: 'My Learning', route: '/my-learning', icon: 'graduation', testId: 'nav-my-learning' },
    { label: 'Certificates', route: '/certificates', icon: 'award', testId: 'nav-certificates' },
    { label: 'Community', route: '/community', icon: 'users', testId: 'nav-community' },
    { label: 'People', route: '/people', icon: 'user', testId: 'nav-people' },
    { label: 'Friends', route: '/friends', icon: 'heart', testId: 'nav-friends' },
    { label: 'Messages', route: '/messages', icon: 'message', testId: 'nav-messages' },
    { label: 'Notifications', route: '/notifications', icon: 'bell', testId: 'nav-notifications' },
  ];

  /** Shown only when the API reported the Admin role. */
  protected readonly adminNav: readonly NavItem[] = [
    { label: 'Overview', route: '/admin', icon: 'shield', testId: 'nav-admin' },
    { label: 'Catalog', route: '/admin/catalog', icon: 'grid', testId: 'nav-admin-catalog' },
    { label: 'Members', route: '/admin/users', icon: 'users', testId: 'nav-admin-users' },
    { label: 'Records', route: '/admin/records', icon: 'file', testId: 'nav-admin-records' },
    { label: 'Pricing', route: '/admin/pricing', icon: 'tag', testId: 'nav-admin-pricing' },
    { label: 'Moderation', route: '/admin/community', icon: 'flag', testId: 'nav-admin-community' },
    { label: 'Operations', route: '/admin/ops', icon: 'wrench', testId: 'nav-admin-ops' },
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

  /** Caps a live badge for display; the full count lives in the page itself. */
  protected badge(count: number): string {
    return count > 99 ? '99+' : `${count}`;
  }
}
