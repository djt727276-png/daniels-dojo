import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { LessonVideoView, MediaVerificationEvidence } from '../../../core/admin/admin-media-api';
import { AdminLessonMedia } from './admin-lesson-media';

const LESSON_ID = '22222222-2222-4222-8222-222222222222';
const VIDEO_URL = `/api/v1/admin/lessons/${LESSON_ID}/video`;

function evidence(overrides: Partial<MediaVerificationEvidence> = {}): MediaVerificationEvidence {
  return {
    cloudPropertiesVerified: false,
    restoreVerified: false,
    providerReady: false,
    adminPlaybackVerifiedAtUtc: null,
    studentPlaybackVerifiedAtUtc: null,
    humanSpotCheckAtUtc: null,
    safeToDeleteLocalOriginal: false,
    ...overrides,
  };
}

function video(overrides: Partial<LessonVideoView> = {}): LessonVideoView {
  return {
    lessonId: LESSON_ID,
    videoId: null,
    status: 'None',
    providerMode: 'Deterministic',
    isPlayable: false,
    durationSeconds: null,
    aspectRatio: null,
    failureCode: null,
    currentSource: null,
    incomingSource: null,
    captions: [],
    verification: evidence(),
    rowVersion: null,
    ...overrides,
  };
}

function fullyVerified(): LessonVideoView {
  return video({
    videoId: '33333333-3333-4333-8333-333333333333',
    status: 'Ready',
    isPlayable: true,
    durationSeconds: 42,
    aspectRatio: '16:9',
    currentSource: {
      id: '44444444-4444-4444-8444-444444444444',
      containerName: 'media-source',
      blobName: 'courses/a/lessons/b/video/c.mp4',
      contentLength: 2048,
      contentType: 'video/mp4',
      checksumSha256: 'abc123def456abc123def456abc123def456abc123def456abc123def456abcd',
      state: 'Current',
      propertiesVerifiedAtUtc: '2026-08-04T12:00:00+00:00',
      restoreVerifiedAtUtc: '2026-08-04T12:01:00+00:00',
      restoreVerifiedLength: 2048,
    },
    verification: evidence({
      cloudPropertiesVerified: true,
      restoreVerified: true,
      providerReady: true,
      adminPlaybackVerifiedAtUtc: '2026-08-04T12:02:00+00:00',
      studentPlaybackVerifiedAtUtc: '2026-08-04T12:03:00+00:00',
      humanSpotCheckAtUtc: '2026-08-04T12:04:00+00:00',
      safeToDeleteLocalOriginal: true,
    }),
  });
}

/** A FileList-shaped stand-in, because jsdom cannot construct a real one. */
function fileList(file: File): FileList {
  return {
    length: 1,
    item: (index: number) => (index === 0 ? file : null),
    0: file,
    [Symbol.iterator]: function* () {
      yield file;
    },
  } as unknown as FileList;
}

/** Picks a file the way a person would, and lets the component take it from there. */
function choose(fixture: ComponentFixture<AdminLessonMedia>, file: File): void {
  const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;

  Object.defineProperty(input, 'files', { value: fileList(file), configurable: true });
  input.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

function setup(): {
  readonly fixture: ComponentFixture<AdminLessonMedia>;
  readonly http: HttpTestingController;
} {
  TestBed.configureTestingModule({
    imports: [AdminLessonMedia],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: new Map([['lessonId', LESSON_ID]]) } },
      },
    ],
  });

  const fixture = TestBed.createComponent(AdminLessonMedia);
  const http = TestBed.inject(HttpTestingController);

  return { fixture, http };
}

describe('AdminLessonMedia', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('tells an author which checks have not passed yet', () => {
    const { fixture, http } = setup();

    fixture.detectChanges();
    http.expectOne(VIDEO_URL).flush(video({ status: 'Ready', isPlayable: true }));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Keep your local original');
    expect(text).toContain('Not yet passed:');
    expect(text).not.toContain('Every check has passed');
  });

  it('only says the original is safe to delete once every check has passed', () => {
    const { fixture, http } = setup();

    fixture.detectChanges();
    http.expectOne(VIDEO_URL).flush(fullyVerified());
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Every check has passed');
    expect(text).not.toContain('Keep your local original');
  });

  it('offers the human sign-off only once the automated checks are through', () => {
    const { fixture, http } = setup();

    fixture.detectChanges();
    http.expectOne(VIDEO_URL).flush(
      video({
        status: 'Ready',
        isPlayable: true,
        verification: evidence({
          cloudPropertiesVerified: true,
          restoreVerified: true,
          providerReady: true,
          adminPlaybackVerifiedAtUtc: '2026-08-04T12:02:00+00:00',
          studentPlaybackVerifiedAtUtc: '2026-08-04T12:03:00+00:00',
        }),
      }),
    );
    fixture.detectChanges();

    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];

    expect(buttons.some((button) => button.textContent?.includes('right footage'))).toBe(true);
  });

  it('says plainly that a replacement does not interrupt students', () => {
    const { fixture, http } = setup();

    fixture.detectChanges();
    http.expectOne(VIDEO_URL).flush(video({ status: 'Replacing', isPlayable: true }));
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Students keep watching');
  });

  it('uploads to the location the ticket names, using the headers it was given', () => {
    const { fixture, http } = setup();

    fixture.detectChanges();
    http.expectOne(VIDEO_URL).flush(video());
    fixture.detectChanges();

    choose(fixture, new File(['bytes'], 'master.mp4', { type: 'video/mp4' }));

    http.expectOne(`${VIDEO_URL}/upload`).flush({
      sessionId: '55555555-5555-4555-8555-555555555555',
      uploadUri: '/api/media/deterministic-upload/media-source/courses/a/master.mp4',
      httpMethod: 'PUT',
      requiredHeaders: { 'Content-Type': 'video/mp4' },
      expiresAtUtc: '2026-08-04T14:00:00+00:00',
      providerMode: 'Deterministic',
    });

    // Exactly as issued: a cloud signature covers the method, the URI, and the headers.
    const upload = http.expectOne(
      '/api/media/deterministic-upload/media-source/courses/a/master.mp4',
    );

    expect(upload.request.method).toBe('PUT');
    expect(upload.request.headers.get('Content-Type')).toBe('video/mp4');

    upload.flush(null, { status: 201, statusText: 'Created' });

    // Completion asks the server to check what actually landed rather than trusting this page.
    http
      .expectOne(
        '/api/v1/admin/media/upload-sessions/55555555-5555-4555-8555-555555555555/complete',
      )
      .flush(video({ status: 'MuxIngesting' }));

    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('being processed');
  });

  it('reports a refused upload without claiming anything was stored', () => {
    const { fixture, http } = setup();

    fixture.detectChanges();
    http.expectOne(VIDEO_URL).flush(video());
    fixture.detectChanges();

    choose(fixture, new File(['bytes'], 'master.mp4', { type: 'video/mp4' }));

    http
      .expectOne(`${VIDEO_URL}/upload`)
      .flush(
        {
          detail: 'The largest accepted upload is smaller than that.',
          code: 'media.upload_too_large',
        },
        { status: 400, statusText: 'Bad Request' },
      );

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('The largest accepted upload is smaller than that.');
    expect(text).toContain('Keep your local original');
  });
});
