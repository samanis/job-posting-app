import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

export function startApplication(): Promise<void> {
  return bootstrapApplication(App, appConfig)
    .then(() => undefined)
    .catch((error: unknown) => console.error(error));
}

void startApplication();
