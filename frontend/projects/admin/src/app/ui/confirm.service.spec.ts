import { TestBed } from '@angular/core/testing';
import { ConfirmService } from './confirm.service';

describe('ConfirmService', () => {
  let service: ConfirmService;

  beforeEach(() => {
    service = TestBed.inject(ConfirmService);
  });

  it('resolves true when confirmed and closes the dialog', async () => {
    const answer = service.ask({ title: 'Delete?', message: 'Sure?' });
    expect(service.pending()?.title).toBe('Delete?');

    service.pending()!.resolve(true);

    expect(await answer).toBe(true);
    expect(service.pending()).toBeNull();
  });

  it('resolves false when cancelled', async () => {
    const answer = service.ask({ title: 'Delete?', message: 'Sure?' });

    service.pending()!.resolve(false);

    expect(await answer).toBe(false);
  });

  it('cancels the first question when a second one is asked', async () => {
    const first = service.ask({ title: 'One', message: '' });
    const second = service.ask({ title: 'Two', message: '' });

    expect(await first).toBe(false);
    expect(service.pending()?.title).toBe('Two');

    service.pending()!.resolve(true);
    expect(await second).toBe(true);
  });
});
