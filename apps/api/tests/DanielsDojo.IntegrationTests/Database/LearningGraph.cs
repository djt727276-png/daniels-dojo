using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Infrastructure.Persistence;

namespace DanielsDojo.IntegrationTests.Database;

/// <summary>A saved user, course, section, and lesson for learning-side tests.</summary>
internal sealed class LearningGraph
{
    public Guid UserId { get; private init; }

    public Guid CourseId { get; private init; }

    public Guid SectionId { get; private init; }

    public Guid LessonId { get; private init; }

    public static async Task<LearningGraph> CreateAsync(DanielsDojoDbContext context)
    {
        User user = TestEntities.User();
        Course course = TestEntities.Course();
        CourseSection section = TestEntities.Section(course.Id, 1);
        Lesson lesson = TestEntities.Lesson(course.Id, section.Id);

        context.Users.Add(user);
        context.Courses.Add(course);
        context.CourseSections.Add(section);
        context.Lessons.Add(lesson);
        await context.SaveChangesAsync();

        return new LearningGraph
        {
            UserId = user.Id,
            CourseId = course.Id,
            SectionId = section.Id,
            LessonId = lesson.Id,
        };
    }
}
