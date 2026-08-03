import { Component } from '@angular/core';

import { SystemStatusCard } from '../system-status/system-status-card';

@Component({
  selector: 'app-home',
  imports: [SystemStatusCard],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  protected readonly title = "Daniel's Dojo";
}
