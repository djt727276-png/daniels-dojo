import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-shell',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
  private readonly auth = inject(AuthService);

  protected readonly productName = "Daniel's Dojo";

  /** Admin navigation visibility. Sourced from the API response, never from the token. */
  protected readonly isAdmin = this.auth.isAdmin;

  protected readonly session = this.auth.session;
}
