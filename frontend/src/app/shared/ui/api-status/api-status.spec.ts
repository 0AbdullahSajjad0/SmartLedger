import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ServerState, ServerStatus } from '../../../core/api/server-status';
import { ApiStatus } from './api-status';

describe('ApiStatus', () => {
  const state = signal<ServerState>('checking');
  const server = { state, startedAt: signal(Date.now()), wake: vi.fn(), retry: vi.fn() };

  beforeEach(() => {
    state.set('checking');
    TestBed.configureTestingModule({ imports: [ApiStatus], providers: [{ provide: ServerStatus, useValue: server }] });
  });

  async function render() {
    const fixture = TestBed.createComponent(ApiStatus);
    await fixture.whenStable();
    return fixture;
  }

  it('makes sure the server is being woken', async () => {
    await render();

    expect(server.wake).toHaveBeenCalled();
  });

  it('follows the shared server check', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;

    state.set('waking');
    await fixture.whenStable();
    expect(element.textContent).toContain('SERVER · WAKING UP, UP TO A MINUTE');

    state.set('ready');
    await fixture.whenStable();
    expect(element.textContent).toContain('SERVER · ONLINE');
  });

  it('offers a retry when the server is unreachable', async () => {
    state.set('unreachable');
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('SERVER · UNREACHABLE');
    element.querySelector('button')!.click();
    expect(server.retry).toHaveBeenCalled();
  });
});
