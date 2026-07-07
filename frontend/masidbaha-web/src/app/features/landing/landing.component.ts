import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { PulsePinComponent } from '../../shared/components/pulse-pin/pulse-pin.component';
import { DeviceMockupComponent } from '../../shared/components/device-mockup/device-mockup.component';

interface FeatureCardData {
  icon: 'pin' | 'check' | 'clock' | 'shield';
  tone: 'water' | 'alert' | 'violet' | 'teal';
  title: string;
  description: string;
}

interface GalleryCardData {
  variant: 'dark' | 'light';
  eyebrow: string;
  title: string;
  description: string;
}

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [CommonModule, RouterLink, PulsePinComponent, DeviceMockupComponent],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss']
})
export class LandingComponent {
  readonly currentYear = new Date().getFullYear();

  constructor(private router: Router) {}

  readonly features: FeatureCardData[] = [
    {
      icon: 'pin',
      tone: 'water',
      title: 'Mag-ulat sa isang tap',
      description: 'I-drop ang pin sa mapa, piliin ang lalim ng baha, tapos na — walang account na kailangan gawin.'
    },
    {
      icon: 'check',
      tone: 'alert',
      title: 'I-verify ng komunidad',
      description: 'Kumpirmahin o markahang tapos na ang isang ulat. Ang confidence score ay galing sa totoong mga tao sa lugar.'
    },
    {
      icon: 'clock',
      tone: 'violet',
      title: 'Awtomatikong nawawala',
      description: 'Nawawala sa mapa ang mga lumang ulat pagkalipas ng ilang oras, kaya laging updated ang nakikita mo.'
    },
    {
      icon: 'shield',
      tone: 'teal',
      title: 'Walang delay, walang paperwork',
      description: 'Direktang broadcast sa lahat ng nakabukas ng mapa — makikita agad ang bagong ulat, live.'
    }
  ];

  readonly galleryCards: GalleryCardData[] = [
    {
      variant: 'dark',
      eyebrow: 'Marikina, Metro Manila',
      title: 'Baha sa Marcos Highway, waist-level',
      description: '48 kumpirmasyon mula sa komunidad sa loob ng 20 minuto.'
    },
    {
      variant: 'light',
      eyebrow: 'Cainta, Rizal',
      title: 'Flash flood malapit sa Sto. Niño',
      description: 'Na-expire na — walang bagong ulat sa nakalipas na 6 na oras.'
    },
    {
      variant: 'dark',
      eyebrow: 'Iloilo City',
      title: 'Baha sa Diversion Road, knee-level',
      description: 'Aktibong sinusubaybayan ngayon ng 120+ residente.'
    },
    {
      variant: 'light',
      eyebrow: 'Davao City',
      title: 'Malubhang pagbaha sa Bankerohan',
      description: 'Na-resolve na matapos ang 3 magkakasunod na "wala na" na boto.'
    }
  ];

  goToMap(): void {
    this.router.navigate(['/map']);
  }
}