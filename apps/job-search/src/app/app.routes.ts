import { Routes } from '@angular/router';
import { JobListPage } from './features/job-search/job-list-page';
export const routes: Routes = [
 { path: '', pathMatch: 'full', redirectTo: 'jobs' },
 { path: 'jobs', title: 'Find jobs', component: JobListPage },
 { path: 'jobs/:id', title: 'Job details', loadComponent: () => import('./features/job-search/job-detail-page').then(m => m.JobDetailPage) },
 { path: '**', title: 'Page not found', loadComponent: () => import('./not-found-page').then(m => m.NotFoundPage) }
];
