import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import * as L from 'leaflet';

// leaflet.heat (used by the analytics/heatmap page) is a UMD module that
// expects a *global* `window.L` to attach itself to — it doesn't know how
// to hook into `import * as L from 'leaflet'`. Since the analytics page is
// lazy-loaded, this assignment must happen here, at app bootstrap, so
// `window.L` is already set by the time that lazy chunk (and its
// `import 'leaflet.heat'`) ever gets evaluated. Without this, the heat
// layer silently never attaches to L and the map shows no coloring at all.
(window as any).L = L;

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
