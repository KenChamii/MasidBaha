import { Component } from '@angular/core';
import { TranslatePipe } from '@masidbaha/shared';

import { PulsePinComponent } from '../pulse-pin/pulse-pin.component';

@Component({
  selector: 'app-device-mockup',
  imports: [PulsePinComponent, TranslatePipe],
  templateUrl: './device-mockup.component.html',
  styleUrls: ['./device-mockup.component.scss']
})
export class DeviceMockupComponent {}
