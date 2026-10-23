import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from './api-config';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SKIP_ERROR_TOAST } from '../core/error-toast.interceptor';

export interface PhotoUploadResult {
  url: string;
}

@Injectable({ providedIn: 'root' })
export class PhotoService {
  private http = inject(HttpClient);

  private readonly baseUrl = `${inject(API_BASE_URL)}/api/v1/photos`;

  // quiet: the caller reports a failure itself (no error toast from the interceptor)
  upload(file: File, options: { quiet?: boolean } = {}): Observable<PhotoUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    const context = options.quiet ? new HttpContext().set(SKIP_ERROR_TOAST, true) : undefined;
    return this.http.post<PhotoUploadResult>(this.baseUrl, formData, { context });
  }
}
