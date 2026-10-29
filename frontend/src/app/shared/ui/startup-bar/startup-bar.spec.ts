import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ServerState, ServerStatus } from '../../../core/api/server-status';
import { StartupBar } from './startup-bar';

describe('StartupBar', () => {
  const state = signal<ServerState>('checking');
  const startedAt = signal(Date.now());
  const server = { state, startedAt, wake: vi.fn(), retry: vi.fn() };

  beforeEach(() => {
    state.set('checking');
    TestBed.configureTestingModule({ imports: [StartupBar], providers: [{ provide: ServerStatus, useValue: server }] });
  });

  async function render() {
    const fixture = TestBed.createComponent(StartupBar);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('stays out of the way while the server answers quickly, and once it is ready', async () => {
    const { fixture, element } = await render();
    expect(element.textContent?.trim()).toBe('');

    state.set('ready');
    await fixture.whenStable();
    expect(element.textContent?.trim()).toBe('');
  });

  it('explains a cold start, counting the seconds', async () => {
    startedAt.set(Date.now() - 12_000);
    state.set('waking');
    const { element } = await render();

    expect(element.querySelector('[role="status"]')?.textContent).toContain('Starting up');
    expect(element.textContent).toContain('Waking it takes up to a minute.');
    expect(element.querySelector('.bar__time')?.textContent).toBe('0:12');
  });

  it('offers to try again when the server never answered', async () => {
    state.set('unreachable');
    const { element } = await render();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain("Can't reach the server");
    element.querySelector('button')!.click();
    expect(server.retry).toHaveBeenCalled();
  });
});
