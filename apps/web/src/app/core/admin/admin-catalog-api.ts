import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResult } from '../catalog/catalog.model';
import { API_BASE_PATH } from '../configuration/app-config';
import {
  AdminCourseDetail,
  AdminCourseListItem,
  AdminTag,
  CreateCourseRequest,
  CreateLessonRequest,
  CreateSectionRequest,
  PublicationStatus,
  ReorderItem,
  StatusChangeRequest,
  UpdateCourseRequest,
  UpdateLessonRequest,
  UpdateSectionRequest,
} from './admin-catalog.model';

/** Filters for the Admin course list. */
export interface AdminCourseFilters {
  readonly search: string;
  readonly status: string;
  readonly page: number;
  readonly pageSize: number;
}

/**
 * Typed client for the Admin catalog endpoints.
 *
 * Every mutation returns the whole course, so a caller replaces its state wholesale and always
 * holds the current row version for the course and every section and lesson beneath it. That is
 * what keeps a second edit from failing on a token the client had no way to refresh.
 */
@Injectable({ providedIn: 'root' })
export class AdminCatalogApi {
  private readonly http = inject(HttpClient);
  private readonly root = `${inject(API_BASE_PATH)}/v1/admin/catalog`;

  listCourses(filters: Partial<AdminCourseFilters>): Observable<PagedResult<AdminCourseListItem>> {
    let params = new HttpParams();

    if (filters.search) {
      params = params.set('search', filters.search);
    }
    if (filters.status) {
      params = params.set('status', filters.status);
    }
    if (filters.page) {
      params = params.set('page', filters.page);
    }
    if (filters.pageSize) {
      params = params.set('pageSize', filters.pageSize);
    }

    return this.http.get<PagedResult<AdminCourseListItem>>(`${this.root}/courses`, { params });
  }

  getCourse(courseId: string): Observable<AdminCourseDetail> {
    return this.http.get<AdminCourseDetail>(`${this.root}/courses/${courseId}`);
  }

  createCourse(request: CreateCourseRequest): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(`${this.root}/courses`, request);
  }

  updateCourse(courseId: string, request: UpdateCourseRequest): Observable<AdminCourseDetail> {
    return this.http.put<AdminCourseDetail>(`${this.root}/courses/${courseId}`, request);
  }

  changeCourseStatus(
    courseId: string,
    target: PublicationStatus,
    request: StatusChangeRequest,
  ): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(
      `${this.root}/courses/${courseId}/status/${target}`,
      request,
    );
  }

  setCourseTags(
    courseId: string,
    tagIds: readonly string[],
    rowVersion: string,
  ): Observable<AdminCourseDetail> {
    return this.http.put<AdminCourseDetail>(`${this.root}/courses/${courseId}/tags`, {
      tagIds,
      rowVersion,
    });
  }

  createSection(courseId: string, request: CreateSectionRequest): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(`${this.root}/courses/${courseId}/sections`, request);
  }

  updateSection(
    courseId: string,
    sectionId: string,
    request: UpdateSectionRequest,
  ): Observable<AdminCourseDetail> {
    return this.http.put<AdminCourseDetail>(
      `${this.root}/courses/${courseId}/sections/${sectionId}`,
      request,
    );
  }

  changeSectionStatus(
    courseId: string,
    sectionId: string,
    target: PublicationStatus,
    request: StatusChangeRequest,
  ): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(
      `${this.root}/courses/${courseId}/sections/${sectionId}/status/${target}`,
      request,
    );
  }

  reorderSections(courseId: string, items: readonly ReorderItem[]): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(`${this.root}/courses/${courseId}/sections/order`, {
      items,
    });
  }

  createLesson(
    courseId: string,
    sectionId: string,
    request: CreateLessonRequest,
  ): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(
      `${this.root}/courses/${courseId}/sections/${sectionId}/lessons`,
      request,
    );
  }

  updateLesson(
    courseId: string,
    lessonId: string,
    request: UpdateLessonRequest,
  ): Observable<AdminCourseDetail> {
    return this.http.put<AdminCourseDetail>(
      `${this.root}/courses/${courseId}/lessons/${lessonId}`,
      request,
    );
  }

  changeLessonStatus(
    courseId: string,
    lessonId: string,
    target: PublicationStatus,
    request: StatusChangeRequest,
  ): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(
      `${this.root}/courses/${courseId}/lessons/${lessonId}/status/${target}`,
      request,
    );
  }

  reorderLessons(
    courseId: string,
    sectionId: string,
    items: readonly ReorderItem[],
  ): Observable<AdminCourseDetail> {
    return this.http.post<AdminCourseDetail>(
      `${this.root}/courses/${courseId}/sections/${sectionId}/lessons/order`,
      { items },
    );
  }

  listTags(): Observable<readonly AdminTag[]> {
    return this.http.get<readonly AdminTag[]>(`${this.root}/tags`);
  }

  createTag(name: string): Observable<AdminTag> {
    return this.http.post<AdminTag>(`${this.root}/tags`, { name });
  }
}
