import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PhotoUploadResult {
  url: string;
}

@Injectable({ providedIn: 'root' })
export class PhotoService {
  private readonly baseUrl = `${environment.apiUrl}/api/photos`;

  constructor(private http: HttpClient) {}

  upload(file: File): Observable<PhotoUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<PhotoUploadResult>(this.baseUrl, formData);
  }
}
