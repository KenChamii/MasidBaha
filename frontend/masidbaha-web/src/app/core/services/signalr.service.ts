import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { FloodReport } from '../../shared/models/flood-report.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class SignalRService {
  private hubConnection!: signalR.HubConnection;

  readonly newReport$ = new Subject<FloodReport>();
  readonly reportUpdated$ = new Subject<{ floodReportId: string; confidenceScore: number; status: number }>();
  readonly removeReport$ = new Subject<string>();
  readonly reconnected$ = new Subject<void>();

  connect(): void {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/flood`)
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('NewReport', (report: FloodReport) => this.newReport$.next(report));
    this.hubConnection.on('ReportUpdated', (update) => this.reportUpdated$.next(update));
    this.hubConnection.on('RemoveReport', (id: string) => this.removeReport$.next(id));
    this.hubConnection.onreconnected(() => this.reconnected$.next());

    this.hubConnection.start();
  }
}