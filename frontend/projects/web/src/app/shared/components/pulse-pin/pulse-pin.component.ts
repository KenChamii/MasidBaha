import { Component, Input } from '@angular/core';

export type PulsePinTone = 'water' | 'alert';
export type PulsePinSize = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-pulse-pin',
  imports: [],
  templateUrl: './pulse-pin.component.html',
  styleUrls: ['./pulse-pin.component.scss']
})
export class PulsePinComponent {
  @Input() tone: PulsePinTone = 'water';
  @Input() size: PulsePinSize = 'md';
}
