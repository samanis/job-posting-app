import { bootstrapApplication } from '@angular/platform-browser';
import { App } from '../src/app/app';
import { appConfig } from '../src/app/app.config';

vi.mock('@angular/platform-browser', async importOriginal => {
  const original = await importOriginal<typeof import('@angular/platform-browser')>();
  return { ...original, bootstrapApplication: vi.fn() };
});

describe('Browser entry point', () => {
  afterEach(() => vi.restoreAllMocks());

  it('bootstraps the application with its production providers', async () => {
    vi.mocked(bootstrapApplication).mockResolvedValue({} as Awaited<ReturnType<typeof bootstrapApplication>>);
    await import('../src/main');
    expect(bootstrapApplication).toHaveBeenCalledWith(App, appConfig);
  });

  it('reports a failed bootstrap', async () => {
    const error = new Error('Bootstrap failed');
    vi.mocked(bootstrapApplication).mockRejectedValue(error);
    const log = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const { startApplication } = await import('../src/main');
    await startApplication();
    expect(log).toHaveBeenCalledWith(error);
  });
});
