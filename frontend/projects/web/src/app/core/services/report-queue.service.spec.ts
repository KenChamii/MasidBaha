import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { FloodReportService, I18nService, PhotoService, ToastService, provideI18n } from '@masidbaha/shared';
import { of, throwError } from 'rxjs';
import { WEB_MESSAGES } from '../i18n/messages';
import { MAX_ATTEMPTS, MAX_QUEUED_REPORTS, ReportQueueService, isRetryable } from './report-queue.service';
import { MemoryQueueStorage, ReportQueueStorage } from './report-queue.storage';

const failure = (status: number) => throwError(() => new HttpErrorResponse({ status }));
const draft = (id: string, extra: object = {}) => ({ id, lat: 14.6, lng: 121, severity: 2 as const, ...extra });

describe('isRetryable', () => {
  it.each([0, 408, 429, 500, 502, 503, 504])('retries after a %d', status => {
    expect(isRetryable(new HttpErrorResponse({ status }))).toBe(true);
  });

  it.each([400, 403, 404, 413, 422])('gives up after a %d', status => {
    expect(isRetryable(new HttpErrorResponse({ status }))).toBe(false);
  });
});

describe('ReportQueueService', () => {
  let create: ReturnType<typeof vi.fn>;
  let upload: ReturnType<typeof vi.fn>;
  let storage: MemoryQueueStorage;
  let service: ReportQueueService;
  let toast: ToastService;
  let online: boolean;

  beforeEach(() => {
    online = true;
    vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online);

    create = vi.fn().mockReturnValue(of({}));
    upload = vi.fn().mockReturnValue(of({ url: 'https://api.example/uploads/a.jpg' }));
    storage = new MemoryQueueStorage();

    TestBed.configureTestingModule({
      providers: [
        provideI18n(WEB_MESSAGES),
        { provide: ReportQueueStorage, useValue: storage },
        { provide: FloodReportService, useValue: { create } },
        { provide: PhotoService, useValue: { upload } }
      ]
    });
    TestBed.inject(I18nService);
    toast = TestBed.inject(ToastService);
    service = TestBed.inject(ReportQueueService);
  });

  afterEach(() => vi.restoreAllMocks());

  it('keeps a report until it can be sent, and remembers it on the device', async () => {
    await service.enqueue(draft('a'));

    expect(service.pending().map(r => r.id)).toEqual(['a']);
    expect((await storage.list()).map(r => r.id)).toEqual(['a']);
  });

  it('sends the report with its own id, then forgets it', async () => {
    await service.enqueue(draft('a', { notes: 'Water is rising' }));

    await service.flush();

    expect(create).toHaveBeenCalledWith(
      { lat: 14.6, lng: 121, severity: 2, notes: 'Water is rising', photoUrl: undefined, clientRequestId: 'a' },
      { quiet: true }
    );
    expect(service.pending()).toEqual([]);
    expect(await storage.list()).toEqual([]);
  });

  it('says so when a saved report went out, and lets the map refresh', async () => {
    const sent = vi.fn();
    service.sent$.subscribe(sent);
    await service.enqueue(draft('a'));

    await service.flush();

    expect(toast.toasts().some(t => t.kind === 'success')).toBe(true);
    expect(sent).toHaveBeenCalledTimes(1);
  });

  it('sends in the order they were made', async () => {
    await service.enqueue(draft('first'));
    await service.enqueue(draft('second'));

    await service.flush();

    expect(create.mock.calls.map(call => call[0].clientRequestId)).toEqual(['first', 'second']);
  });

  it('sends nothing while offline', async () => {
    await service.enqueue(draft('a'));
    online = false;

    await service.flush();

    expect(create).not.toHaveBeenCalled();
    expect(service.pending()).toHaveLength(1);
  });

  it('keeps the report, and the ones behind it, when the server cannot be reached', async () => {
    create.mockReturnValue(failure(0));
    await service.enqueue(draft('a'));
    await service.enqueue(draft('b'));

    await service.flush();

    expect(service.pending().map(r => r.id)).toEqual(['a', 'b']);
    expect(create).toHaveBeenCalledTimes(1); // b waits for a
    expect(service.pending()[0].attempts).toBe(1);
  });

  it('sends it later, with the same id, once the connection is back', async () => {
    create.mockReturnValueOnce(failure(503)).mockReturnValue(of({}));
    await service.enqueue(draft('a'));

    await service.flush();
    await service.flush();

    expect(create.mock.calls.map(call => call[0].clientRequestId)).toEqual(['a', 'a']);
    expect(service.pending()).toEqual([]);
  });

  it('drops a report the server refuses, and says so, without blocking the others', async () => {
    create.mockReturnValueOnce(failure(400)).mockReturnValue(of({}));
    await service.enqueue(draft('bad'));
    await service.enqueue(draft('good'));

    await service.flush();

    expect(service.pending()).toEqual([]);
    expect(toast.toasts().some(t => t.kind === 'error')).toBe(true);
    expect(create).toHaveBeenCalledTimes(2);
  });

  it('gives up on a report that keeps failing', async () => {
    create.mockReturnValue(failure(500));
    await service.enqueue(draft('a'));

    for (let i = 0; i < MAX_ATTEMPTS; i++) await service.flush();

    expect(service.pending()).toEqual([]);
  });

  it('uploads the photo first and sends its address with the report', async () => {
    const photo = new File(['x'], 'flood.jpg', { type: 'image/jpeg' });
    await service.enqueue(draft('a', { photo }));

    await service.flush();

    expect(upload).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][0].photoUrl).toBe('https://api.example/uploads/a.jpg');
  });

  it('does not upload the photo again when only the report failed', async () => {
    create.mockReturnValueOnce(failure(0)).mockReturnValue(of({}));
    await service.enqueue(draft('a', { photo: new File(['x'], 'flood.jpg', { type: 'image/jpeg' }) }));

    await service.flush();
    await service.flush();

    expect(upload).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[1][0].photoUrl).toBe('https://api.example/uploads/a.jpg');
  });

  it('sends the report without its photo when the photo is refused', async () => {
    upload.mockReturnValue(failure(400));
    await service.enqueue(draft('a', { photo: new File(['x'], 'flood.jpg', { type: 'image/jpeg' }) }));

    await service.flush();

    expect(create).toHaveBeenCalledTimes(1);
    expect(create.mock.calls[0][0].photoUrl).toBeUndefined();
    expect(service.pending()).toEqual([]);
  });

  it('refuses more than the limit', async () => {
    for (let i = 0; i < MAX_QUEUED_REPORTS; i++) expect(await service.enqueue(draft(`r${i}`))).toBe(true);

    expect(await service.enqueue(draft('one-too-many'))).toBe(false);
  });

  it('picks up what an earlier visit left behind', async () => {
    await storage.put({ ...draft('old'), createdAt: '2026-01-01T00:00:00Z', attempts: 0 });

    await service.start();

    expect(create).toHaveBeenCalledWith(expect.objectContaining({ clientRequestId: 'old' }), { quiet: true });
    expect(service.pending()).toEqual([]);
  });
});
