import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SkeletonRowComponent } from './skeleton-row.component';

@Component({
  imports: [SkeletonRowComponent],
  template: `<table>
    <tbody>
      <tr appSkeletonRow [columns]="4"></tr>
    </tbody>
  </table>`
})
class HostComponent {}

describe('SkeletonRowComponent', () => {
  it('draws one grey cell per column', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('tbody tr td .skeleton')).toHaveLength(4);
  });

  it('hides the placeholders from screen readers', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('td').getAttribute('aria-hidden')).toBe('true');
  });
});
