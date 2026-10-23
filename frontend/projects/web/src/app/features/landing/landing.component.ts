import { Component, inject } from '@angular/core';

import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@masidbaha/shared';
import { PulsePinComponent } from '../../shared/components/pulse-pin/pulse-pin.component';
import { DeviceMockupComponent } from '../../shared/components/device-mockup/device-mockup.component';

// title and description are message keys (see core/i18n/messages.ts)
interface FeatureCardData {
  icon: 'pin' | 'check' | 'clock' | 'shield';
  tone: 'water' | 'alert' | 'violet' | 'teal';
  title: string;
  description: string;
}

interface GalleryCardData {
  variant: 'dark' | 'light';
  // a place name, the same in every language
  eyebrow: string;
  title: string;
  description: string;
}

@Component({
  selector: 'app-landing',
  imports: [RouterLink, PulsePinComponent, DeviceMockupComponent, TranslatePipe],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss']
})
export class LandingComponent {
  private router = inject(Router);

  readonly features: FeatureCardData[] = [
    {
      icon: 'pin',
      tone: 'water',
      title: 'feature.report.title',
      description: 'feature.report.desc'
    },
    {
      icon: 'check',
      tone: 'alert',
      title: 'feature.verified.title',
      description: 'feature.verified.desc'
    },
    {
      icon: 'clock',
      tone: 'violet',
      title: 'feature.clearing.title',
      description: 'feature.clearing.desc'
    },
    {
      icon: 'shield',
      tone: 'teal',
      title: 'feature.live.title',
      description: 'feature.live.desc'
    }
  ];

  readonly galleryCards: GalleryCardData[] = [
    {
      variant: 'dark',
      eyebrow: 'Marikina, Metro Manila',
      title: 'gallery.1.title',
      description: 'gallery.1.desc'
    },
    {
      variant: 'light',
      eyebrow: 'Cainta, Rizal',
      title: 'gallery.2.title',
      description: 'gallery.2.desc'
    },
    {
      variant: 'dark',
      eyebrow: 'Iloilo City',
      title: 'gallery.3.title',
      description: 'gallery.3.desc'
    },
    {
      variant: 'light',
      eyebrow: 'Davao City',
      title: 'gallery.4.title',
      description: 'gallery.4.desc'
    }
  ];

  goToMap(): void {
    this.router.navigate(['/map']);
  }
}
