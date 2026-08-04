import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { LessonPreview } from './lesson-preview';

const PREVIEW_URL = '/api/v1/catalog/courses/atlas/lessons/intro/preview';

function setup() {
  TestBed.configureTestingModule({
    imports: [LessonPreview],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            paramMap: new Map([
              ['courseSlug', 'atlas'],
              ['lessonSlug', 'intro'],
            ]),
          },
        },
      },
    ],
  });

  return {
    fixture: TestBed.createComponent(LessonPreview),
    http: TestBed.inject(HttpTestingController),
  };
}

describe('LessonPreview', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders the stored body as text', () => {
    const { fixture, http } = setup();

    http.expectOne(PREVIEW_URL).flush({
      courseSlug: 'atlas',
      courseTitle: 'Atlas Enterprise Developer',
      lessonSlug: 'intro',
      title: 'Introduction',
      summary: 'A short intro.',
      body: 'First line.\nSecond line.',
    });
    fixture.detectChanges();

    const body = fixture.nativeElement.querySelector('[data-testid="preview-body"]');
    expect(body?.textContent).toContain('First line.');
    expect(body?.textContent).toContain('Second line.');
  });

  it('never renders markup from the stored body as HTML', () => {
    const { fixture, http } = setup();

    http.expectOne(PREVIEW_URL).flush({
      courseSlug: 'atlas',
      courseTitle: 'Atlas Enterprise Developer',
      lessonSlug: 'intro',
      title: 'Introduction',
      summary: null,
      // Content an attacker might store. It must be displayed literally, not parsed.
      body: '<img src=x onerror="alert(1)"><script>alert(2)</script>**not bold**',
    });
    fixture.detectChanges();

    const host: HTMLElement = fixture.nativeElement;
    const preview = host.querySelector('[data-testid="preview-body"]')!;

    expect(preview.querySelector('img')).toBeNull();
    expect(preview.querySelector('script')).toBeNull();
    expect(host.querySelector('strong')).toBeNull();

    // The characters are all present as text.
    expect(preview.textContent).toContain('<img src=x');
    expect(preview.textContent).toContain('**not bold**');
  });

  it('shows an unavailable state on 404 without revealing why', () => {
    const { fixture, http } = setup();

    http.expectOne(PREVIEW_URL).flush('', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent ?? '';
    expect(text).toContain('Preview not available');
    expect(text).not.toContain('draft');
    expect(text).not.toContain('unpublished');
  });

  it('shows a recoverable error for other failures', () => {
    const { fixture, http } = setup();

    http.expectOne(PREVIEW_URL).flush('', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('could not load this preview');
  });
});
