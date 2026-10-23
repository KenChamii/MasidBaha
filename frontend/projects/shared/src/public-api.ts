/*
 * Public API of @masidbaha/shared: what both the public site and the admin
 * app may use. Anything not exported here is private to the library.
 */

// API clients and configuration
export * from './lib/api/api-config';
export * from './lib/api/flood-report.service';
export * from './lib/api/photo.service';
export * from './lib/api/admin.service';
export * from './lib/api/admin-auth.service';

// Models
export * from './lib/models/flood-report.model';
export * from './lib/models/severity';
export * from './lib/models/admin.model';

// Core: theme and HTTP interceptors
export * from './lib/core/theme.service';
export * from './lib/core/api-credentials.interceptor';
export * from './lib/core/error-toast.interceptor';

// UI
export * from './lib/ui/brand/brand.component';
export * from './lib/ui/theme-toggle/theme-toggle.component';
export * from './lib/ui/modal/modal.component';
export * from './lib/ui/toast/toast.service';
export * from './lib/ui/toast/toast-container.component';

// Utilities
export * from './lib/utils/time-ago';
