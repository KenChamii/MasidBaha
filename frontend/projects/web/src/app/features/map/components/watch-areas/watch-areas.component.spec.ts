import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideI18n } from '@masidbaha/shared';
import { of } from 'rxjs';
import { WEB_MESSAGES } from '../../../../core/i18n/messages';
import { PushNotificationService } from '../../../../core/services/push-notification.service';
import { WatchArea, WatchAreaService } from '../../../../core/services/watch-area.service';
import { WatchAreasComponent } from './watch-areas.component';

const home: WatchArea = { id: 'a1', name: 'Home', lat: 14.65, lng: 121.1, radiusMeters: 2000 };

describe('WatchAreasComponent', () => {
  let fixture: ComponentFixture<WatchAreasComponent>;
  let add: ReturnType<typeof vi.fn>;

  async function create(options: { subscribed?: boolean; supported?: boolean; areas?: WatchArea[] } = {}) {
    const { subscribed = true, supported = true, areas = [] } = options;
    add = vi.fn().mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      providers: [
        provideI18n(WEB_MESSAGES),
        {
          provide: PushNotificationService,
          useValue: { isSupported: supported, subscription$: of(subscribed ? {} : null) }
        },
        {
          provide: WatchAreaService,
          useValue: {
            areas: signal(areas),
            load: vi.fn().mockResolvedValue(undefined),
            clear: vi.fn(),
            add,
            remove: vi.fn().mockResolvedValue(undefined)
          }
        }
      ]
    });

    fixture = TestBed.createComponent(WatchAreasComponent);
    fixture.componentRef.setInput('center', { lat: 14.6, lng: 121.0 });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const button = () => fixture.nativeElement.querySelector('.watch-btn') as HTMLButtonElement | null;

  async function openPanel() {
    button()!.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('is hidden until alerts are turned on', async () => {
    await create({ subscribed: false });

    expect(button()).toBeNull();
  });

  it('is hidden where push is not supported', async () => {
    await create({ supported: false });

    expect(button()).toBeNull();
  });

  it('appears once alerts are on, and opens a panel that explains the default', async () => {
    await create();
    expect(button()).not.toBeNull();

    await openPanel();

    expect(fixture.nativeElement.querySelector('.watch').textContent).toContain('every new report in the country');
  });

  it('adds the middle of the map with the chosen name and radius', async () => {
    await create();
    await openPanel();

    const input = fixture.nativeElement.querySelector('input[name=name]') as HTMLInputElement;
    input.value = 'Office';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(add).toHaveBeenCalledWith('Office', 14.6, 121.0, 2000);
  });

  it('lists the saved places and stops offering to add at the limit', async () => {
    const five = Array.from({ length: 5 }, (_, i) => ({ ...home, id: `a${i}`, name: `Place ${i}` }));
    await create({ areas: five });
    await openPanel();

    const panel: HTMLElement = fixture.nativeElement.querySelector('.watch');
    expect(panel.querySelectorAll('.watch__list li').length).toBe(5);
    expect(panel.querySelector('form')).toBeNull();
    expect(panel.textContent).toContain('up to 5 places');
  });

  it('shows the reason when the server refuses a place', async () => {
    await create();
    add.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: { errors: { Lat: ['Watch areas must be located within the Philippines.'] } }
      })
    );
    await openPanel();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('within the Philippines');
  });
});
