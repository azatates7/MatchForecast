import { Component } from '@angular/core';

/*
 * Site logosu: yeşil zemin üzerinde futbol topu. Renkler temadan gelir (--pitch zemin, --surface top), böylece açık ve koyu temada kontrast korunur. Dekoratif olduğu için ekran okuyuculardan gizlenir; yanındaki site adı zaten okunur.
 */
@Component({
  selector: 'app-brand-logo',
  host: { style: 'display: contents' },
  template: `
    <svg class="brand-logo" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <rect width="32" height="32" rx="8" class="brand-logo-bg" />
      <g class="brand-logo-ball">
        <circle cx="16" cy="16" r="9" fill="none" stroke-width="2" />
        <path d="M16 12.5l3.33 2.42-1.27 3.91h-4.12l-1.27-3.91z" />
        <path d="M16 12.5V7M19.33 14.92l5.23-1.7M18.06 18.83l3.23 4.45M13.94 18.83l-3.23 4.45M12.67 14.92l-5.23-1.7"
              fill="none" stroke-width="1.6" stroke-linecap="round" />
      </g>
    </svg>
  `,
})
export class BrandLogo {}