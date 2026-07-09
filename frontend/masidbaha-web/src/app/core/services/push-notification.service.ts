import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SessionService } from './session.service';

export interface PushSubscriptionRequest {
  sessionId: string;
  endpoint: string;
  keys: { p256dh: string; auth: string };
}

// Wraps Angular's SwPush and talks to our backend to subscribe/unsubscribe.
// A component just calls subscribe() or unsubscribe(), it doesn't need to
// know about VAPID keys or the subscription payload shape.
//
// Needs a production build (ng build, not ng serve), since the service
// worker is only registered when not in dev mode. isSupported will be
// false during local dev with ng serve.
@Injectable({ providedIn: 'root' })
export class PushNotificationService {
  private readonly baseUrl = `${environment.apiUrl}/api/push`;

  constructor(
    private http: HttpClient,
    private swPush: SwPush,
    private sessionService: SessionService
  ) {}

  get isSupported(): boolean {
    return this.swPush.isEnabled;
  }

  // Emits the current subscription (or null) whenever it changes.
  get subscription$() {
    return this.swPush.subscription;
  }

  async getCurrentSubscription(): Promise<globalThis.PushSubscription | null> {
    return firstValueFrom(this.swPush.subscription);
  }

  async subscribe(): Promise<void> {
    if (!this.isSupported) {
      throw new Error('Hindi supported ang push notifications dito (kailangan ng production build at HTTPS/localhost).');
    }

    const { publicKey } = await firstValueFrom(
      this.http.get<{ publicKey: string }>(`${this.baseUrl}/vapid-public-key`)
    );

    if (!publicKey) {
      throw new Error('Hindi pa na-configure ang push notifications sa server na ito.');
    }

    const subscription = await this.swPush.requestSubscription({ serverPublicKey: publicKey });
    const json = subscription.toJSON();

    await firstValueFrom(this.http.post(`${this.baseUrl}/subscribe`, {
      sessionId: this.sessionService.sessionId,
      endpoint: json.endpoint ?? subscription.endpoint,
      keys: {
        p256dh: json.keys?.['p256dh'] ?? '',
        auth: json.keys?.['auth'] ?? ''
      }
    } satisfies PushSubscriptionRequest));
  }

  async unsubscribe(): Promise<void> {
    // Grab the endpoint before unsubscribing on the browser side, since
    // the browser forgets it right after.
    const current = await this.getCurrentSubscription();

    if (current) {
      await firstValueFrom(this.http.post(`${this.baseUrl}/unsubscribe`, { endpoint: current.endpoint }));
    }

    await this.swPush.unsubscribe();
  }
}
