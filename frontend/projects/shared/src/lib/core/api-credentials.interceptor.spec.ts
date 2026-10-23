import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api/api-config';
import { apiCredentialsInterceptor } from './api-credentials.interceptor';

function setup(apiUrl: string) {
  TestBed.configureTestingModule({
    providers: [
      { provide: API_BASE_URL, useValue: apiUrl },
      provideHttpClient(withInterceptors([apiCredentialsInterceptor])),
      provideHttpClientTesting()
    ]
  });
  return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController) };
}

describe('apiCredentialsInterceptor', () => {
  it('sends cookies to an absolute API address', () => {
    const { http, backend } = setup('https://api.example.com');

    http.get('https://api.example.com/api/v1/x').subscribe();

    expect(backend.expectOne('https://api.example.com/api/v1/x').request.withCredentials).toBe(true);
  });

  it('does not send cookies to anyone else', () => {
    const { http, backend } = setup('https://api.example.com');

    http.get('https://third-party.example.net/data').subscribe();

    expect(backend.expectOne('https://third-party.example.net/data').request.withCredentials).toBe(false);
  });

  it('treats root-relative URLs as the API when it is same-origin', () => {
    const { http, backend } = setup('');

    http.get('/api/v1/x').subscribe();
    http.get('https://third-party.example.net/data').subscribe();

    expect(backend.expectOne('/api/v1/x').request.withCredentials).toBe(true);
    expect(backend.expectOne('https://third-party.example.net/data').request.withCredentials).toBe(false);
  });
});
