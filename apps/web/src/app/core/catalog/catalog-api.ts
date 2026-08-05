import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_PATH } from '../configuration/app-config';
import {
  CatalogFilters,
  CourseCard,
  CourseDetail,
  LessonPreview,
  PagedResult,
  PublicPrice,
} from './catalog.model';

/** Typed client for the anonymous catalog endpoints. */
@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private readonly http = inject(HttpClient);
  private readonly basePath = inject(API_BASE_PATH);

  /**
   * Lists published courses. Only non-empty filters are sent, so the request URL stays clean
   * and the server applies its own defaults.
   */
  listCourses(filters: Partial<CatalogFilters>): Observable<PagedResult<CourseCard>> {
    let params = new HttpParams();

    if (filters.search) {
      params = params.set('search', filters.search);
    }
    if (filters.level) {
      params = params.set('level', filters.level);
    }
    if (filters.tag) {
      params = params.set('tag', filters.tag);
    }
    if (filters.page) {
      params = params.set('page', filters.page);
    }
    if (filters.pageSize) {
      params = params.set('pageSize', filters.pageSize);
    }

    return this.http.get<PagedResult<CourseCard>>(`${this.basePath}/v1/catalog/courses`, {
      params,
    });
  }

  /** Fetches one published course. A 404 means "not available", never "exists but hidden". */
  getCourse(slug: string): Observable<CourseDetail> {
    return this.http.get<CourseDetail>(
      `${this.basePath}/v1/catalog/courses/${encodeURIComponent(slug)}`,
    );
  }

  /** The live membership price. A 404 means none is published, which the UI says honestly. */
  getMembershipPrice(): Observable<PublicPrice> {
    return this.http.get<PublicPrice>(`${this.basePath}/v1/catalog/membership`);
  }

  /** Fetches a preview lesson's plain-text body. */
  getLessonPreview(courseSlug: string, lessonSlug: string): Observable<LessonPreview> {
    return this.http.get<LessonPreview>(
      `${this.basePath}/v1/catalog/courses/${encodeURIComponent(courseSlug)}` +
        `/lessons/${encodeURIComponent(lessonSlug)}/preview`,
    );
  }
}
