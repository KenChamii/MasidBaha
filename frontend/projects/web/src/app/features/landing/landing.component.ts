import { Component, inject } from '@angular/core';

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
  imports: [RouterLink, PulsePinComponent, DeviceMockupComponent],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss']
})
export class LandingComponent {
  private router = inject(Router);

  readonly features: FeatureCardData[] = [
    {
      icon: 'pin',
      tone: 'water',
      title: 'Report in one tap',
      description: 'Drop a pin on the map and select the flood depth. No account or sign-up is required.'
    },
    {
      icon: 'check',
      tone: 'alert',
      title: 'Community verified',
      description:
        'Residents confirm or clear each report, and the confidence score reflects what people nearby are seeing.'
    },
    {
      icon: 'clock',
      tone: 'violet',
      title: 'Self-clearing reports',
      description: 'Reports expire automatically after a few hours, so the map only shows current conditions.'
    },
    {
      icon: 'shield',
      tone: 'teal',
      title: 'Real-time updates',
      description: 'New reports appear instantly for everyone with the map open, with no refresh needed.'
    }
  ];

  readonly galleryCards: GalleryCardData[] = [
    {
      variant: 'dark',
      eyebrow: 'Marikina, Metro Manila',
      title: 'Waist-deep flooding on Marcos Highway',
      description: '48 community confirmations within 20 minutes.'
    },
    {
      variant: 'light',
      eyebrow: 'Cainta, Rizal',
      title: 'Flash flood near Sto. Niño',
      description: 'Expired after no new reports in 6 hours.'
    },
    {
      variant: 'dark',
      eyebrow: 'Iloilo City',
      title: 'Knee-deep flooding on Diversion Road',
      description: 'Currently being tracked by more than 120 residents.'
    },
    {
      variant: 'light',
      eyebrow: 'Davao City',
      title: 'Severe flooding in Bankerohan',
      description: 'Resolved after 3 consecutive "cleared" votes.'
    }
  ];

  goToMap(): void {
    this.router.navigate(['/map']);
  }
}
