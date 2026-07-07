import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class GeolocationService {
  getCurrentPosition(): Promise<{ lat: number; lng: number }> {
    return new Promise((resolve, reject) => {
      if (!navigator.geolocation) {
        reject(new Error('Geolocation not supported'));
        return;
      }
      navigator.geolocation.getCurrentPosition(
        (position) => resolve({ lat: position.coords.latitude, lng: position.coords.longitude }),
        (error) => reject(error)
      );
    });
  }
}