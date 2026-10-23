import { HttpErrorResponse } from '@angular/common/http';
import { locationOf, problemMessage } from './util';

describe('problemMessage', () => {
  it('prefers the first validation message', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { errors: { Code: ['That code is not valid.'] }, detail: 'x' }
    });

    expect(problemMessage(error)).toBe('That code is not valid.');
  });

  it('falls back to the problem detail', () => {
    const error = new HttpErrorResponse({ status: 409, error: { detail: 'Already banned.' } });

    expect(problemMessage(error)).toBe('Already banned.');
  });

  it('explains a 403 in plain words', () => {
    expect(problemMessage(new HttpErrorResponse({ status: 403 }))).toContain('role');
  });

  it('uses the fallback for anything else', () => {
    expect(problemMessage(new Error('boom'), 'Nope')).toBe('Nope');
  });
});

describe('locationOf', () => {
  it('names the most specific place, then falls back to coordinates', () => {
    expect(locationOf({ city: 'Marikina', province: 'Metro Manila', lat: 1, lng: 2 })).toBe('Marikina');
    expect(locationOf({ lat: 14.65071, lng: 121.10294 })).toBe('14.6507, 121.1029');
  });
});
