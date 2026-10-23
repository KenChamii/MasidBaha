import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from './api-config';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface PhotoUploadResult {
  url: string;
}

@Injectable({ providedIn: 'root' })
export class PhotoService {
  private http = inject(HttpClient);

  private readonly baseUrl = `${inject(API_BASE_URL)}/api/v1/photos`;

  upload(file: File): Observable<PhotoUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<PhotoUploadResult>(this.baseUrl, formData);
  }
}
