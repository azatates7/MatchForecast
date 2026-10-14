import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    // API'ye gerçek istek atılmasın: token ve boş maç listesi döner.
    globalThis.fetch = (async (url: string) =>
      new Response(JSON.stringify(String(url).includes('/auth/token') ? { accessToken: 't' } : []), { status: 200 })) as typeof fetch;

    await TestBed.configureTestingModule({
      imports: [App],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render brand', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('MatchForecast');
  });
});
