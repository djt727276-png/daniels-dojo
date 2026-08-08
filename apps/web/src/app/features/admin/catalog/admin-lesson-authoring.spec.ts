import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import {
  AdminCourseDetail,
  AdminLesson,
  AdminSection,
} from '../../../core/admin/admin-catalog.model';
import { AdminCourseWorkspace } from './admin-course-workspace';

/**
 * The authoring path an instructor actually walks: name a lesson, choose its type, and — for a
 * video — find somewhere to put the file.
 *
 * The slug is deliberately absent from these expectations. It is a routing detail the server
 * derives from the title, and the moment it reappears in the creation payload the author is
 * back to inventing URL segments.
 */

const COURSE_ID = '11111111-1111-4111-8111-111111111111';
const SECTION_ID = '22222222-2222-4222-8222-222222222222';
const LESSON_ID = '33333333-3333-4333-8333-333333333333';
const COURSE_URL = `/api/v1/admin/catalog/courses/${COURSE_ID}`;
const TAGS_URL = '/api/v1/admin/catalog/tags';
const LESSONS_URL = `${COURSE_URL}/sections/${SECTION_ID}/lessons`;

function lesson(overrides: Partial<AdminLesson> = {}): AdminLesson {
  return {
    id: LESSON_ID,
    slug: 'introduction-to-csharp',
    title: 'Introduction to C#',
    summary: null,
    lessonType: 'Video',
    bodyMarkdown: null,
    sortOrder: 1,
    isPreview: false,
    status: 'Draft',
    estimatedDurationSeconds: null,
    videoStatus: null,
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

function section(lessons: readonly AdminLesson[] = []): AdminSection {
  return {
    id: SECTION_ID,
    title: 'Introduction',
    description: null,
    sortOrder: 1,
    status: 'Draft',
    lessons,
    rowVersion: 'AAAAAAAAB9E=',
  };
}

function course(sections: readonly AdminSection[]): AdminCourseDetail {
  return {
    id: COURSE_ID,
    slug: 'atlas-enterprise-developer',
    title: 'Atlas Enterprise Developer',
    summary: 'A summary.',
    description: 'A description.',
    level: 'AllLevels',
    status: 'Draft',
    includedInMembership: true,
    imageAltText: null,
    publishedAtUtc: null,
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    updatedAtUtc: '2026-01-01T00:00:00+00:00',
    slugLocked: false,
    sections,
    tags: [],
    rowVersion: 'AAAAAAAAB9E=',
  };
}

function setup(detail: AdminCourseDetail) {
  TestBed.configureTestingModule({
    imports: [AdminCourseWorkspace],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: new Map([['courseId', COURSE_ID]]) } },
      },
    ],
  });

  const fixture = TestBed.createComponent(AdminCourseWorkspace);
  const http = TestBed.inject(HttpTestingController);

  fixture.detectChanges();
  http.expectOne(COURSE_URL).flush(detail);
  http.expectOne(TAGS_URL).flush([]);
  fixture.detectChanges();

  return { fixture, http, element: fixture.nativeElement as HTMLElement };
}

function setValue(element: HTMLElement, testId: string, value: string): void {
  const input = element.querySelector<HTMLInputElement>(`[data-testid="${testId}"]`)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('lesson authoring', () => {
  it('offers no slug field when adding a lesson', () => {
    const { element } = setup(course([section()]));

    expect(element.querySelector('[data-testid="new-lesson-title"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="new-lesson-slug"]')).toBeNull();
  });

  it('creates a lesson from a title alone, sending no slug', () => {
    const { fixture, http, element } = setup(course([section()]));

    setValue(element, 'new-lesson-title', 'Introduction to C#');
    fixture.detectChanges();

    element.querySelector<HTMLButtonElement>(`[data-testid="add-lesson-${SECTION_ID}"]`)!.click();

    const request = http.expectOne(LESSONS_URL);

    expect(request.request.method).toBe('POST');
    expect(request.request.body.title).toBe('Introduction to C#');
    expect(request.request.body.lessonType).toBe('Article');
    expect(request.request.body.slug).toBeUndefined();

    request.flush(course([section([lesson({ lessonType: 'Article' })])]));
    http.verify();
  });

  it('opens the new lesson editor so the author lands on its content', () => {
    const { fixture, http, element } = setup(course([section()]));

    setValue(element, 'new-lesson-title', 'Introduction to C#');
    fixture.detectChanges();
    element.querySelector<HTMLButtonElement>(`[data-testid="add-lesson-${SECTION_ID}"]`)!.click();

    http.expectOne(LESSONS_URL).flush(course([section([lesson()])]));
    fixture.detectChanges();

    // The editor is open without a second click: its video panel is on screen.
    expect(
      element.querySelector('[data-testid="lesson-video-introduction-to-csharp"]'),
    ).not.toBeNull();
  });
});

describe('lesson video panel', () => {
  /** Opens the editor for the single lesson in the fixture. */
  function open(detail: AdminCourseDetail) {
    const harness = setup(detail);
    const slug = detail.sections[0].lessons[0].slug;

    harness.element
      .querySelector<HTMLButtonElement>(`[data-testid="lesson-edit-${slug}"]`)!
      .click();
    harness.fixture.detectChanges();

    return harness;
  }

  it('invites an upload when a video lesson has no asset', () => {
    const { element } = open(course([section([lesson({ videoStatus: null })])]));

    const action = element.querySelector('[data-testid="lesson-video-introduction-to-csharp"]')!;

    expect(action.textContent?.trim()).toBe('Upload video');
    expect(action.getAttribute('href')).toBe(`/admin/lessons/${LESSON_ID}/media`);
    expect(element.textContent).toContain('No video uploaded yet.');
  });

  it('reports processing while the provider works', () => {
    const { element } = open(course([section([lesson({ videoStatus: 'Processing' })])]));

    expect(element.textContent).toContain('being processed');
    expect(
      element
        .querySelector('[data-testid="lesson-video-introduction-to-csharp"]')
        ?.textContent?.trim(),
    ).toBe('Manage video');
  });

  it('reports a ready video', () => {
    const { element } = open(course([section([lesson({ videoStatus: 'Ready' })])]));

    expect(element.textContent).toContain('Ready to play.');
  });

  it('explains a failure and that the master is retained', () => {
    const { element } = open(course([section([lesson({ videoStatus: 'Failed' })])]));

    expect(element.textContent).toContain('Processing failed');
    expect(element.textContent).toContain('can be retried');
  });

  it('shows the body editor for an article instead of video controls', () => {
    const { element } = open(
      course([section([lesson({ lessonType: 'Article', slug: 'written-lesson' })])]),
    );

    expect(element.querySelector('[data-testid="lesson-body"]')).not.toBeNull();
    expect(element.querySelector('[data-testid="lesson-video-written-lesson"]')).toBeNull();
  });

  it('keeps the derived slug editable under Advanced rather than in the main form', () => {
    const { element } = open(course([section([lesson()])]));

    const advanced = element.querySelector('details')!;

    expect(advanced.textContent).toContain('Advanced');
    expect(advanced.querySelector('[data-testid="lesson-slug-input"]')).not.toBeNull();
  });
});
