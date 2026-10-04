import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'jobs/new' },
  { path: 'jobs/new', title: 'Post a job', loadComponent: () => import('./features/job-posting/components/new-job-page').then(module => module.NewJobPage) },
  { path: '**', title: 'Page not found', loadComponent: () => import('./not-found-page').then(module => module.NotFoundPage) },
];
