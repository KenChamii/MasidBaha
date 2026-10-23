// What happens when someone taps "share" on a report

// The page a shared link opens first. It carries the report's preview card for Facebook and
// friends, then moves the visitor on to the map (see frontend/functions/r/[id].js).
export function reportShareUrl(origin: string, reportId: string): string {
  return `${origin.replace(/\/$/, '')}/r/${reportId}`;
}

export type ShareResult = 'shared' | 'copied' | 'cancelled' | 'failed';

export interface ShareTarget {
  id: string;
  // e.g. "Marikina": what the message says the report is about
  place?: string | null;
}

// Uses the phone's own share sheet where there is one (Messenger, Viber, Facebook, SMS...),
// and copies the link everywhere else.
export async function shareReport(target: ShareTarget, origin: string): Promise<ShareResult> {
  const url = reportShareUrl(origin, target.id);
  const text = target.place ? `Flood report in ${target.place}` : 'Flood report';

  if (typeof navigator.share === 'function') {
    try {
      await navigator.share({ title: 'MasidBaha', text, url });
      return 'shared';
    } catch (error) {
      // Closing the share sheet is not a failure
      if (error instanceof DOMException && error.name === 'AbortError') return 'cancelled';
      // Anything else: fall through to copying
    }
  }

  try {
    await navigator.clipboard.writeText(url);
    return 'copied';
  } catch {
    return 'failed';
  }
}
