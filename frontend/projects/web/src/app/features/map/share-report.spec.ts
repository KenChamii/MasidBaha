import { reportShareUrl, shareReport } from './share-report';

const ID = '01a1675d-8d3c-7df1-8f79-2fe928692607';
const ORIGIN = 'https://masidbaha.pages.dev';

describe('reportShareUrl', () => {
  it('points at the shared-link page, not the map', () => {
    expect(reportShareUrl(ORIGIN, ID)).toBe(`${ORIGIN}/r/${ID}`);
  });

  it('ignores a trailing slash on the origin', () => {
    expect(reportShareUrl(`${ORIGIN}/`, ID)).toBe(`${ORIGIN}/r/${ID}`);
  });
});

describe('shareReport', () => {
  const original = { share: navigator.share, clipboard: navigator.clipboard };

  afterEach(() => {
    Object.defineProperty(navigator, 'share', { value: original.share, configurable: true });
    Object.defineProperty(navigator, 'clipboard', { value: original.clipboard, configurable: true });
  });

  function stub(share: unknown, writeText?: unknown) {
    Object.defineProperty(navigator, 'share', { value: share, configurable: true });
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
  }

  it("uses the device's share sheet when there is one", async () => {
    const share = vi.fn().mockResolvedValue(undefined);
    stub(share);

    const result = await shareReport({ id: ID, place: 'Marikina' }, ORIGIN);

    expect(result).toBe('shared');
    expect(share).toHaveBeenCalledWith({
      title: 'MasidBaha',
      text: 'Flood report in Marikina',
      url: `${ORIGIN}/r/${ID}`
    });
  });

  it('does not treat closing the share sheet as a failure, and does not copy behind their back', async () => {
    const writeText = vi.fn();
    stub(vi.fn().mockRejectedValue(new DOMException('closed', 'AbortError')), writeText);

    expect(await shareReport({ id: ID }, ORIGIN)).toBe('cancelled');
    expect(writeText).not.toHaveBeenCalled();
  });

  it('copies the link where there is no share sheet', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stub(undefined, writeText);

    expect(await shareReport({ id: ID }, ORIGIN)).toBe('copied');
    expect(writeText).toHaveBeenCalledWith(`${ORIGIN}/r/${ID}`);
  });

  it('copies the link if sharing fails for another reason', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stub(vi.fn().mockRejectedValue(new Error('not allowed')), writeText);

    expect(await shareReport({ id: ID }, ORIGIN)).toBe('copied');
  });

  it('reports a failure when neither works', async () => {
    stub(undefined, vi.fn().mockRejectedValue(new Error('denied')));

    expect(await shareReport({ id: ID }, ORIGIN)).toBe('failed');
  });
});
