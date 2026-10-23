import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface WatchArea {
  id: string;
  name: string;
  lat: number;
  lng: number;
  radiusMeters: number;
}

export const MAX_WATCH_AREAS = 5;

// The places this visitor wants alerts about. They belong to the anonymous session
// cookie, so there is nothing to sign in to. The list is shared (a signal) so the
// panel and the map circles always agree.
@Injectable({ providedIn: 'root' })
export class WatchAreaService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/v1/push/watch-areas`;

  readonly areas = signal<WatchArea[]>([]);

  async load(): Promise<void> {
    this.areas.set(await firstValueFrom(this.http.get<WatchArea[]>(this.baseUrl)));
  }

  async add(name: string, lat: number, lng: number, radiusMeters: number): Promise<void> {
    const created = await firstValueFrom(this.http.post<WatchArea>(this.baseUrl, { name, lat, lng, radiusMeters }));
    this.areas.update(list => [...list, created]);
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.baseUrl}/${id}`));
    this.areas.update(list => list.filter(area => area.id !== id));
  }

  // Alerts were turned off: the server drops the areas with the subscription
  clear(): void {
    this.areas.set([]);
  }
}
