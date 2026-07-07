import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

export type PulsePinTone = 'water' | 'alert';
export type PulsePinSize = 'sm' | 'md' | 'lg';

@Component({
  selector: 'app-pulse-pin',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pulse-pin.component.html',
  styleUrls: ['./pulse-pin.component.scss']
})
export class PulsePinComponent {
  @Input() tone: PulsePinTone = 'water';
  @Input() size: PulsePinSize = 'md';
}