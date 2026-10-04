import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  template: `<h1>Page not found</h1><p>The requested page is unavailable.</p><a routerLink="/jobs/new">Go to job posting</a>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundPage {}
