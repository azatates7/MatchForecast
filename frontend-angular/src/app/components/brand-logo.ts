import { Component } from '@angular/core';

/*
 * Site logosu: klasik siyah-beyaz futbol topu (sekmedeki ⚽ simgesiyle aynı görünüm).
 * Kenar çizgisi temanın yazı rengini (--ink) alır: koyu temada topun siyah parçaları zemine karışmaz, top yuvarlak görünür.
 * Dekoratif olduğu için ekran okuyuculardan gizlenir; yanındaki site adı zaten okunur.
 * Not: frontend/src/components/BrandLogo.tsx ile aynı çizimdir, birlikte güncelleyin.
 */
@Component({
  selector: 'app-brand-logo',
  host: { style: 'display: contents' },
  template: `
    <svg class="brand-logo" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <defs>
        <clipPath id="mf-ball-clip"><circle cx="16" cy="16" r="15" /></clipPath>
        <radialGradient id="mf-ball-shade" cx="0.38" cy="0.32" r="0.75">
          <stop offset="0.55" stop-color="#ffffff" />
          <stop offset="1" stop-color="#d5dbe1" />
        </radialGradient>
      </defs>
      <circle cx="16" cy="16" r="15" fill="url(#mf-ball-shade)" />
      <g clip-path="url(#mf-ball-clip)" fill="#1b1b1b" stroke="#1b1b1b" stroke-width="0.9" stroke-linejoin="round" stroke-linecap="round">
        <path d="M16 10.6l5.1 3.7-1.9 6.1h-6.4l-1.9-6.1z" />
        <path d="M16 4.4l-4.4-3.2 1.7-5.1h5.4l1.7 5.1z" />
        <path d="M27 12.4l1.7-5.1h5.4l1.7 5.1-4.4 3.2z" />
        <path d="M22.8 25.4h5.4l1.7 5.1-4.4 3.2-4.4-3.2z" />
        <path d="M9.2 25.4l1.7 5.1-4.4 3.2-4.4-3.2 1.7-5.1z" />
        <path d="M5 12.4l-4.4 3.2-4.4-3.2 1.7-5.1h5.4z" />
        <path fill="none" d="M16 10.6V4.4M21.1 14.3l5.9-1.9M19.2 20.4l3.6 5M12.8 20.4l-3.6 5M10.9 14.3L5 12.4" />
      </g>
      <circle cx="16" cy="16" r="15.1" fill="none" class="brand-logo-rim" stroke-width="0.9" />
    </svg>
  `,
})
export class BrandLogo {}