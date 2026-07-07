import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PulsePinComponent } from '../pulse-pin/pulse-pin.component';

@Component({
  selector: 'app-device-mockup',
  standalone: true,
  imports: [CommonModule, PulsePinComponent],
  templateUrl: './device-mockup.component.html',
  styleUrls: ['./device-mockup.component.scss']
})
export class DeviceMockupComponent {}