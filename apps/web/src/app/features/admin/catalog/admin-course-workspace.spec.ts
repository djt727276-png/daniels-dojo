import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';

import { AdminCourseDetail } from '../../../core/admin/admin-catalog.model';
import { AdminCourseWorkspace } from './admin-course-workspace';

const COURSE_ID = '11111111-1111-4111-8111-111111111111';
const COURSE_URL = `/api/v1/admin/catalog/courses/${COURSE_ID}`;
const TAGS_URL = '/api/v1/admin/catalog/tags';

function course(overrides: Partial<AdminCourseDetail> = {}): AdminCourseDetail {
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
    sections: [],
    tags: [],
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

function setup() {
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

  return { fixture, http };
}

/** The rendered DOM, typed so query selectors return real elements. */
function host(fixture: { nativeElement: HTMLElement }): HTMLElement {
  return fixture.nativeElement;
}

/** Answers the two requests the workspace makes on construction. */
function load(http: HttpTestingController, detail: AdminCourseDetail): void {
  http.expectOne(COURSE_URL).flush(detail);
  http.expectOne(TAGS_URL).flush([]);
}

describe('AdminCourseWorkspace', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('offers only the transitions the status graph allows', () => {
    const { fixture, http } = setup();
    load(http, course({ status: 'Archived' }));
    fixture.detectChanges();

    // Archived is a one-way door back to draft.
    expect(host(fixture).querySelector('[data-testid="course-draft"]')).not.toBeNull();
    expect(host(fixture).querySelector('[data-testid="course-published"]')).toBeNull();
  });

  it('locks the slug field once the course has been published', () => {
    const { fixture, http } = setup();
    load(
      http,
      course({
        status: 'Published',
        publishedAtUtc: '2026-02-01T00:00:00+00:00',
        slugLocked: true,
      }),
    );
    fixture.detectChanges();

    const slug = host(fixture).querySelector<HTMLInputElement>('[data-testid="edit-slug"]')!;

    expect(slug.disabled).toBe(true);
    expect(host(fixture).textContent).toContain('Fixed since this course was first');
  });

  it('sends the row version it was given and replaces state from the response', () => {
    const { fixture, http } = setup();
    load(http, course());
    fixture.detectChanges();

    host(fixture).querySelector<HTMLButtonElement>('[data-testid="save-course"]')!.click();

    const request = http.expectOne(COURSE_URL);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.rowVersion).toBe('AAAAAAAAB9E=');

    request.flush(course({ title: 'Renamed by the server', rowVersion: 'AAAAAAAAB9I=' }));
    fixture.detectChanges();

    // The next write must carry the refreshed token, not the one the page loaded with.
    host(fixture).querySelector<HTMLButtonElement>('[data-testid="save-course"]')!.click();
    const second = http.expectOne(COURSE_URL);
    expect(second.request.body.rowVersion).toBe('AAAAAAAAB9I=');
    second.flush(course({ rowVersion: 'AAAAAAAAB9I=' }));
  });

  it('offers a reload rather than a retry when the record changed underneath', () => {
    const { fixture, http } = setup();
    load(http, course());
    fixture.detectChanges();

    host(fixture).querySelector<HTMLButtonElement>('[data-testid="save-course"]')!.click();

    http.expectOne(COURSE_URL).flush(
      {
        title: 'Conflicting change',
        detail: 'This record changed after you loaded it.',
        code: 'platform.concurrency_conflict',
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(host(fixture).querySelector('[data-testid="stale-warning"]')).not.toBeNull();
    expect(host(fixture).querySelector('[data-testid="reload-course"]')).not.toBeNull();
  });

  it('shows field-level messages returned by the API', () => {
    const { fixture, http } = setup();
    load(http, course());
    fixture.detectChanges();

    host(fixture).querySelector<HTMLButtonElement>('[data-testid="save-course"]')!.click();

    http.expectOne(COURSE_URL).flush(
      {
        title: 'One or more validation errors occurred.',
        code: 'platform.validation_failed',
        errors: { slug: ['Use 3 to 128 characters.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    const summary = host(fixture).querySelector('[data-testid="form-error-summary"]');
    expect(summary?.textContent).toContain('Use 3 to 128 characters.');
  });

  it('sends the complete new order when a section is moved', () => {
    const { fixture, http } = setup();
    load(
      http,
      course({
        sections: [
          {
            id: 'a1111111-1111-4111-8111-111111111111',
            title: 'One',
            description: null,
            sortOrder: 0,
            status: 'Draft',
            lessons: [],
            rowVersion: 'AAAAAAAAAAE=',
          },
          {
            id: 'b1111111-1111-4111-8111-111111111111',
            title: 'Two',
            description: null,
            sortOrder: 1,
            status: 'Draft',
            lessons: [],
            rowVersion: 'AAAAAAAAAAI=',
          },
        ],
      }),
    );
    fixture.detectChanges();

    host(fixture)
      .querySelector<HTMLButtonElement>(
        '[data-testid="section-down-a1111111-1111-4111-8111-111111111111"]',
      )!
      .click();

    const request = http.expectOne(`${COURSE_URL}/sections/order`);
    expect(request.request.body.items.map((item: { id: string }) => item.id)).toEqual([
      'b1111111-1111-4111-8111-111111111111',
      'a1111111-1111-4111-8111-111111111111',
    ]);

    request.flush(course());
  });

  it('reports a missing course without implying it exists', () => {
    const { fixture, http } = setup();

    http.expectOne(COURSE_URL).flush('', { status: 404, statusText: 'Not Found' });
    http.expectOne(TAGS_URL).flush([]);
    fixture.detectChanges();

    expect(host(fixture).querySelector('[data-testid="course-missing"]')).not.toBeNull();
    expect(host(fixture).textContent).not.toContain('draft');
  });
});
