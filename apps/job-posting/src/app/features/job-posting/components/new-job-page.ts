import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-new-job-page',
  template: `<section aria-labelledby="new-job-heading"><h1 id="new-job-heading">Post a job</h1><p>Create a new opening for your team.</p></section>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NewJobPage {}
