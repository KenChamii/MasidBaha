import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PulsePinComponent } from '../pulse-pin/pulse-pin.component';

@Component({
    selector: 'app-device-mockup',
    imports: [CommonModule, PulsePinComponent],
    templateUrl: './device-mockup.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrls: ['./device-mockup.component.scss']
})
export class DeviceMockupComponent {}