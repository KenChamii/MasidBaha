import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    vi.useFakeTimers();
    service = new ToastService();
  });

  afterEach(() => vi.useRealTimers());

  it('shows a message with its kind', () => {
    service.error('Server is down');

    expect(service.toasts()).toHaveLength(1);
    expect(service.toasts()[0]).toMatchObject({ kind: 'error', message: 'Server is down' });
  });

  it('does not stack the same message twice', () => {
    service.error('Server is down');
    service.error('Server is down');
    service.error('Something else');

    expect(service.toasts().map(t => t.message)).toEqual(['Server is down', 'Something else']);
  });

  it('removes a toast by itself after its duration', () => {
    service.show('Saved', 'success', 3000);

    vi.advanceTimersByTime(2999);
    expect(service.toasts()).toHaveLength(1);

    vi.advanceTimersByTime(1);
    expect(service.toasts()).toHaveLength(0);
  });

  it('keeps a toast until dismissed when the duration is zero', () => {
    service.show('Stay', 'info', 0);

    vi.advanceTimersByTime(60_000);

    expect(service.toasts()).toHaveLength(1);
  });

  it('dismisses a single toast', () => {
    service.error('One');
    service.error('Two');
    const first = service.toasts()[0];

    service.dismiss(first.id);

    expect(service.toasts().map(t => t.message)).toEqual(['Two']);
  });

  it('allows the same message again once it is gone', () => {
    service.show('Again', 'info', 1000);
    vi.advanceTimersByTime(1000);

    service.show('Again', 'info', 1000);

    expect(service.toasts()).toHaveLength(1);
  });
});
