import { Component } from '@angular/core';

import { PulsePinComponent } from '../pulse-pin/pulse-pin.component';

@Component({
  selector: 'app-device-mockup',
  imports: [PulsePinComponent],
  templateUrl: './device-mockup.component.html',
  styleUrls: ['./device-mockup.component.scss']
})
export class DeviceMockupComponent {}
