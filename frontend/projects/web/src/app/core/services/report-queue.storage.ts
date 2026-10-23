import { Injectable } from '@angular/core';
import { Severity } from '@masidbaha/shared';

// A report saved on the device until it can be sent
export interface QueuedReport {
  // Also sent as the report's clientRequestId, so a retry can never make a second report
  id: string;
  lat: number;
  lng: number;
  severity: Severity;
  notes?: string;
  photo?: Blob;
  photoName?: string;
  // Set once the photo is uploaded, so a retry does not upload it again
  photoUrl?: string;
  createdAt: string;
  attempts: number;
}

// Where queued reports live. Swappable, so tests need no browser database.
@Injectable({ providedIn: 'root', useFactory: () => new IndexedDbQueueStorage() })
export abstract class ReportQueueStorage {
  abstract list(): Promise<QueuedReport[]>;
  abstract put(report: QueuedReport): Promise<void>;
  abstract remove(id: string): Promise<void>;
}

export class MemoryQueueStorage extends ReportQueueStorage {
  private readonly items = new Map<string, QueuedReport>();

  async list(): Promise<QueuedReport[]> {
    return [...this.items.values()].sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  }

  async put(report: QueuedReport): Promise<void> {
    this.items.set(report.id, report);
  }

  async remove(id: string): Promise<void> {
    this.items.delete(id);
  }
}

const DB_NAME = 'masidbaha-queue';
const STORE = 'pending-reports';

// IndexedDB keeps photos (as blobs) across a closed tab or a restarted phone. Where it is not available (some
// private modes), the queue still works while the page stays open.
export class IndexedDbQueueStorage extends ReportQueueStorage {
  private readonly fallback = new MemoryQueueStorage();
  private db: Promise<IDBDatabase | null> | null = null;

  private open(): Promise<IDBDatabase | null> {
    this.db ??= new Promise(resolve => {
      try {
        const request = indexedDB.open(DB_NAME, 1);
        request.onupgradeneeded = () => request.result.createObjectStore(STORE, { keyPath: 'id' });
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => resolve(null);
        request.onblocked = () => resolve(null);
      } catch {
        resolve(null);
      }
    });
    return this.db;
  }

  private async run<T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest<T>): Promise<T | null> {
    const db = await this.open();
    if (!db) return null;
    return new Promise(resolve => {
      try {
        const request = action(db.transaction(STORE, mode).objectStore(STORE));
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => resolve(null);
      } catch {
        resolve(null);
      }
    });
  }

  async list(): Promise<QueuedReport[]> {
    const stored = await this.run<QueuedReport[]>('readonly', store => store.getAll());
    const all = [...(stored ?? []), ...(await this.fallback.list())];
    return all.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  }

  async put(report: QueuedReport): Promise<void> {
    const saved = await this.run('readwrite', store => store.put(report));
    if (saved === null) await this.fallback.put(report);
  }

  async remove(id: string): Promise<void> {
    await this.run('readwrite', store => store.delete(id));
    await this.fallback.remove(id);
  }
}
