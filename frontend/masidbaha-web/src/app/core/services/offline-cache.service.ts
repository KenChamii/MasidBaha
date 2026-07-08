import { Injectable } from '@angular/core';
import { FloodReport } from '../../shared/models/flood-report.model';

const DB_NAME = 'masidbaha-offline';
const DB_VERSION = 1;
const STORE_NAME = 'last-known-reports';
const CACHE_KEY = 'nearby';

// Angular's service worker (ngsw) only caches HTTP GET responses and static
// assets — it has no concept of "the last data the app actually rendered".
// This service fills that gap: whenever a live fetch of nearby reports
// succeeds, we snapshot it here. When the network is down, the map page
// reads the snapshot instead, so users see *something* instead of a blank
// map during a flood (the exact moment connectivity is most likely to drop).
@Injectable({ providedIn: 'root' })
export class OfflineCacheService {
  private dbPromise: Promise<IDBDatabase> | null = null;

  private getDb(): Promise<IDBDatabase> {
    if (this.dbPromise) return this.dbPromise;

    this.dbPromise = new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, DB_VERSION);

      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(STORE_NAME)) {
          db.createObjectStore(STORE_NAME);
        }
      };

      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });

    return this.dbPromise;
  }

  async saveLastKnownReports(reports: FloodReport[]): Promise<void> {
    try {
      const db = await this.getDb();
      await new Promise<void>((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, 'readwrite');
        tx.objectStore(STORE_NAME).put(
          { reports, savedAt: new Date().toISOString() },
          CACHE_KEY
        );
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      });
    } catch (err) {
      // Non-fatal — offline caching is a "nice to have"; if IndexedDB is
      // unavailable (e.g. private browsing) the app should keep working
      // normally, just without the offline fallback.
      console.warn('OfflineCacheService: failed to save snapshot', err);
    }
  }

  async getLastKnownReports(): Promise<{ reports: FloodReport[]; savedAt: string } | null> {
    try {
      const db = await this.getDb();
      return await new Promise((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, 'readonly');
        const req = tx.objectStore(STORE_NAME).get(CACHE_KEY);
        req.onsuccess = () => resolve(req.result ?? null);
        req.onerror = () => reject(req.error);
      });
    } catch (err) {
      console.warn('OfflineCacheService: failed to read snapshot', err);
      return null;
    }
  }
}
