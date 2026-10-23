import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { SEVERITY_META, Severity, TranslatePipe } from '@masidbaha/shared';
import { PulsePinComponent } from '../../shared/components/pulse-pin/pulse-pin.component';
import { DeviceMockupComponent } from '../../shared/components/device-mockup/device-mockup.component';

// Everything with a title, text or label below is a message key (see core/i18n/messages.ts)
interface Step {
  index: string;
  title: string;
  text: string;
}

interface TrustRow {
  time: string;
  event: string;
  change: string;
  score: string;
  resolved?: boolean;
}

interface Card {
  icon: 'pin' | 'cloud' | 'layers' | 'wifi' | 'link' | 'globe' | 'phone' | 'shield';
  title: string;
  text: string;
}

interface Layer {
  name: string;
  text: string;
}

interface SpecRow {
  label: string;
  value: string;
}

interface QueueRow {
  place: string;
  depth: 1 | 2 | 3 | 4;
  flag: string;
  up: number;
  down: number;
}

interface AuditRow {
  time: string;
  who: string;
  action: string;
  target: string;
}

export const SOURCE_URL = 'https://github.com/KenChamii/MasidBaha';

@Component({
  selector: 'app-landing',
  imports: [RouterLink, PulsePinComponent, DeviceMockupComponent, TranslatePipe],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss']
})
export class LandingComponent {
  private readonly router = inject(Router);

  protected readonly sourceUrl = SOURCE_URL;

  // The four depths in the report form mock, the third one chosen
  protected readonly depths: { severity: Severity; color: string; chosen: boolean }[] = ([1, 2, 3, 4] as const).map(
    severity => ({ severity, color: SEVERITY_META[severity].color, chosen: severity === 3 })
  );

  protected readonly flow: Step[] = [
    { index: '01', title: 'home.flow.phone.title', text: 'home.flow.phone.text' },
    { index: '02', title: 'home.flow.api.title', text: 'home.flow.api.text' },
    { index: '03', title: 'home.flow.postgis.title', text: 'home.flow.postgis.text' },
    { index: '04', title: 'home.flow.signalr.title', text: 'home.flow.signalr.text' },
    { index: '05', title: 'home.flow.push.title', text: 'home.flow.push.text' }
  ];

  // One report over an afternoon: the score moves with the votes
  protected readonly trust: TrustRow[] = [
    { time: '2:10 PM', event: 'home.trust.e1', change: '+1', score: '1' },
    { time: '2:14 PM', event: 'home.trust.e2', change: '+1', score: '2' },
    { time: '2:31 PM', event: 'home.trust.e3', change: '+1', score: '3' },
    { time: '3:48 PM', event: 'home.trust.e4', change: '-1', score: '2' },
    { time: '4:20 PM', event: 'home.trust.e5', change: '-2', score: '0', resolved: true }
  ];

  protected readonly trustRules = ['home.trust.rule1', 'home.trust.rule2', 'home.trust.rule3', 'home.trust.rule4'];

  protected readonly cards: Card[] = [
    { icon: 'pin', title: 'home.feature.watch.title', text: 'home.feature.watch.text' },
    { icon: 'cloud', title: 'home.feature.weather.title', text: 'home.feature.weather.text' },
    { icon: 'layers', title: 'home.feature.cluster.title', text: 'home.feature.cluster.text' },
    { icon: 'wifi', title: 'home.feature.offline.title', text: 'home.feature.offline.text' },
    { icon: 'link', title: 'home.feature.share.title', text: 'home.feature.share.text' },
    { icon: 'globe', title: 'home.feature.lang.title', text: 'home.feature.lang.text' },
    { icon: 'phone', title: 'home.feature.pwa.title', text: 'home.feature.pwa.text' },
    { icon: 'shield', title: 'home.feature.admin.title', text: 'home.feature.admin.text' }
  ];

  protected readonly route = ['home.stack.route1', 'home.stack.route2', 'home.stack.route3', 'home.stack.route4'];

  // Example data for the moderators section (marked as an example on the page)
  protected readonly queue: QueueRow[] = [
    { place: 'Quezon City', depth: 3, flag: 'home.admin.flag.denies', up: 1, down: 4 },
    { place: 'Davao City', depth: 4, flag: 'home.admin.flag.flooding', up: 0, down: 0 },
    { place: 'Iloilo City', depth: 2, flag: 'home.admin.flag.trust', up: 2, down: 3 }
  ];

  protected readonly queueActions = ['home.admin.act.resolve', 'home.admin.act.hide', 'home.admin.act.expire'];

  protected readonly audit: AuditRow[] = [
    { time: '9:42 AM', who: 'home.admin.who.mod', action: 'report.status_changed', target: 'home.admin.target.report' },
    { time: '9:40 AM', who: 'home.admin.who.mod', action: 'ban.created', target: 'home.admin.target.source' },
    {
      time: '9:15 AM',
      who: 'home.admin.who.admin',
      action: 'broadcast.sent',
      target: 'home.admin.target.announcement'
    },
    { time: '8:58 AM', who: 'home.admin.who.admin', action: 'settings.changed', target: 'home.admin.target.setting' }
  ];

  protected readonly adminItems: Step[] = [
    { index: '', title: 'home.admin.item.queue.title', text: 'home.admin.item.queue.text' },
    { index: '', title: 'home.admin.item.bans.title', text: 'home.admin.item.bans.text' },
    { index: '', title: 'home.admin.item.broadcast.title', text: 'home.admin.item.broadcast.text' },
    { index: '', title: 'home.admin.item.settings.title', text: 'home.admin.item.settings.text' },
    { index: '', title: 'home.admin.item.reports.title', text: 'home.admin.item.reports.text' },
    { index: '', title: 'home.admin.item.roles.title', text: 'home.admin.item.roles.text' }
  ];

  protected readonly layers: Layer[] = [
    { name: 'home.layer.web.name', text: 'home.layer.web.text' },
    { name: 'home.layer.admin.name', text: 'home.layer.admin.text' },
    { name: 'home.layer.shared.name', text: 'home.layer.shared.text' },
    { name: 'home.layer.api.name', text: 'home.layer.api.text' },
    { name: 'home.layer.data.name', text: 'home.layer.data.text' },
    { name: 'home.layer.edge.name', text: 'home.layer.edge.text' }
  ];

  protected readonly spec: SpecRow[] = [
    { label: 'home.spec.backend.label', value: 'home.spec.backend.value' },
    { label: 'home.spec.database.label', value: 'home.spec.database.value' },
    { label: 'home.spec.realtime.label', value: 'home.spec.realtime.value' },
    { label: 'home.spec.frontend.label', value: 'home.spec.frontend.value' },
    { label: 'home.spec.auth.label', value: 'home.spec.auth.value' },
    { label: 'home.spec.api.label', value: 'home.spec.api.value' },
    { label: 'home.spec.tests.label', value: 'home.spec.tests.value' },
    { label: 'home.spec.ci.label', value: 'home.spec.ci.value' },
    { label: 'home.spec.hosting.label', value: 'home.spec.hosting.value' },
    { label: 'home.spec.languages.label', value: 'home.spec.languages.value' }
  ];

  protected readonly privacy: Step[] = [
    { index: '', title: 'home.privacy.accounts.title', text: 'home.privacy.accounts.text' },
    { index: '', title: 'home.privacy.photos.title', text: 'home.privacy.photos.text' },
    { index: '', title: 'home.privacy.ip.title', text: 'home.privacy.ip.text' },
    { index: '', title: 'home.privacy.staff.title', text: 'home.privacy.staff.text' }
  ];

  protected readonly limits = ['home.limits.1', 'home.limits.2', 'home.limits.3', 'home.limits.4'];

  goToMap(): void {
    void this.router.navigate(['/map']);
  }
}
