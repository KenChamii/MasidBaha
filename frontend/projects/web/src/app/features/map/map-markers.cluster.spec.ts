import * as L from 'leaflet';
import { buildClusterIcon, floodMarker, worstSeverity } from './map-markers';

describe('clusters', () => {
  describe('worstSeverity', () => {
    it('is the worst flood inside the group', () => {
      const markers = [
        floodMarker([14.6, 121], 1, false),
        floodMarker([14.61, 121], 3, false),
        floodMarker([14.62, 121], 2, false)
      ];

      expect(worstSeverity(markers)).toBe(3);
    });

    it('is the mildest for a group with nothing worse', () => {
      expect(worstSeverity([floodMarker([14.6, 121], 1, false)])).toBe(1);
    });

    it('copes with a marker that carries no severity', () => {
      expect(worstSeverity([L.marker([14.6, 121])])).toBe(1);
    });
  });

  describe('buildClusterIcon', () => {
    it('shows how many reports are in the cluster', () => {
      expect(String(buildClusterIcon(7, 2).options.html)).toContain('<span>7</span>');
    });

    it('is coloured by the worst flood inside', () => {
      expect(String(buildClusterIcon(4, 4).options.html)).toContain('#E63946');
      expect(String(buildClusterIcon(4, 1).options.html)).toContain('#34C6E8');
    });

    it('caps a very large count so it still fits in the badge', () => {
      expect(String(buildClusterIcon(412, 2).options.html)).toContain('99+');
    });

    it('is bigger for bigger clusters', () => {
      const small = buildClusterIcon(3, 1).options.iconSize as [number, number];
      const large = buildClusterIcon(80, 1).options.iconSize as [number, number];

      expect(large[0]).toBeGreaterThan(small[0]);
    });
  });
});
