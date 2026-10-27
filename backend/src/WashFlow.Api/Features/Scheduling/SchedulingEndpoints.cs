using System.Security.Claims;
using WashFlow.Application.Authorization;
using WashFlow.Application.Scheduling;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Scheduling;

namespace WashFlow.Api.Features.Scheduling;

public static class SchedulingEndpoints
{
    public static void MapSchedulingEndpoints(this WebApplication app)
    {
        var root = app.MapGroup("/api/v1/businesses/{businessId:guid}")
            .RequireAuthorization().RequireRateLimiting("authentication")
            .WithTags("Owner scheduling").WithGroupName("scheduling");
        root.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            var actor = Actor(context.HttpContext.User);
            if (actor == null) return Results.Problem(statusCode: 401);
            var business = Guid.Parse(context.HttpContext.Request.RouteValues["businessId"]!.ToString()!);
            var authorization = context.HttpContext.RequestServices.GetRequiredService<BusinessAuthorizationService>();
            if (!await authorization.HasRoleAsync(actor.UserId, business, MembershipRole.Owner, context.HttpContext.RequestAborted))
                return Results.Problem(statusCode: 403);
            // Phase 9B independently rechecks session/grants under its transaction lock.
            return await next(context);
        });

        Describe<WorkAreaView[]>(root.MapGet("/work-areas", async (Guid businessId, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            Results.Ok(await s.WorkAreas(Actor(p)!, businessId, ct))), "SchedulingWorkAreas", "Çalışma alanlarını listele.");
        Describe<WorkAreaView>(root.MapPost("/work-areas", async (Guid businessId, WorkAreaInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, x => SchedulingValidation.WorkArea(x, false), async x =>
            {
                var value = await s.SaveWorkArea(Actor(p)!, businessId, null, x, ct);
                return Results.Json(value, statusCode: 201);
            })), "SchedulingCreateWorkArea", "Çalışma alanı oluştur.", 201).Accepts<WorkAreaInput>("application/json");
        Describe<WorkAreaView>(root.MapPut("/work-areas/{workAreaId:guid}", async (Guid businessId, Guid workAreaId, WorkAreaInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, x => SchedulingValidation.WorkArea(x, true), async x => Results.Ok(await s.SaveWorkArea(Actor(p)!, businessId, workAreaId, x, ct)))),
            "SchedulingUpdateWorkArea", "Çalışma alanını güncel sürümle güncelle.").Accepts<WorkAreaInput>("application/json");
        DescribeEmpty(root.MapPost("/work-areas/{workAreaId:guid}/archive", async (Guid businessId, Guid workAreaId, VersionInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, SchedulingValidation.Versioned, async x => { await s.ArchiveWorkArea(Actor(p)!, businessId, workAreaId, x, ct); return Results.NoContent(); })),
            "SchedulingArchiveWorkArea", "Kullanımda olmayan çalışma alanını arşivle.").Accepts<VersionInput>("application/json");

        Describe<RequirementView[]>(root.MapGet("/services/{serviceId:guid}/scheduling-requirements", async (Guid businessId, Guid serviceId, string? category, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            Results.Ok(await s.ServiceRequirements(Actor(p)!, businessId, serviceId, category, ct))), "SchedulingRequirements", "Hizmetin gereksinimlerini isteğe bağlı araç türüne göre listele.");
        Describe<RequirementView>(root.MapPut("/services/{serviceId:guid}/offerings/{category}/scheduling-requirement", async (Guid businessId, Guid serviceId, string category, RequirementInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, x => SchedulingValidation.Requirement(x, category, false), async x => Results.Ok(await s.SaveRequirement(Actor(p)!, businessId, serviceId, category, x, ct)))),
            "SchedulingSaveRequirement", "Açık tek aşama gereksinimini oluştur veya sürümle güncelle.").Accepts<RequirementInput>("application/json");
        Describe<RequirementView>(root.MapPost("/services/{serviceId:guid}/offerings/{category}/scheduling-requirement/disable", async (Guid businessId, Guid serviceId, string category, VersionInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, SchedulingValidation.Versioned, async x => Results.Ok(await s.DisableRequirement(Actor(p)!, businessId, serviceId, category, x, ct)))),
            "SchedulingDisableRequirement", "İzin verildiğinde tek aşama onayını geri çekerek gereksinimi devre dışı bırak.").Accepts<VersionInput>("application/json");

        Describe<EmployeeView[]>(root.MapGet("/scheduling-employees", async (Guid businessId, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            Results.Ok(await s.Employees(Actor(p)!, businessId, ct))), "SchedulingEmployees", "Açık seçim için etkin çalışanları listele.");
        Describe<AvailabilityView[]>(root.MapGet("/employees/{membershipId:guid}/availability", async (Guid businessId, Guid membershipId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int? page, int? pageSize, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
        {
            var query = new AvailabilityRangeInput(fromUtc, toUtcExclusive, page ?? 1, pageSize ?? 20);
            var errors = SchedulingValidation.AvailabilityRange(query);
            return errors.Count != 0 ? Results.ValidationProblem(errors) : Results.Ok(await s.DatedAvailability(Actor(p)!, businessId, membershipId, query, ct));
        }), "SchedulingEmployeeAvailability", "En fazla 31 günlük aralıkla, sayfalı çalışan uygunluğunu listele; arşivli satırlar korunur.");
        Describe<AvailabilityView>(root.MapPost("/employees/{membershipId:guid}/availability", async (Guid businessId, Guid membershipId, AvailabilityInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, x => SchedulingValidation.Availability(x, false), async x =>
            {
                var value = await s.SaveAvailability(Actor(p)!, businessId, membershipId, null, x, ct);
                return Results.Json(value, statusCode: 201);
            })), "SchedulingCreateAvailability", "Tarihli UTC uygunluk penceresi oluştur.", 201).Accepts<AvailabilityInput>("application/json");
        Describe<AvailabilityView>(root.MapPut("/employees/{membershipId:guid}/availability/{availabilityId:guid}", async (Guid businessId, Guid membershipId, Guid availabilityId, AvailabilityInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, x => SchedulingValidation.Availability(x, true), async x => Results.Ok(await s.SaveAvailability(Actor(p)!, businessId, membershipId, availabilityId, x, ct)))),
            "SchedulingUpdateAvailability", "Uygunluğu güncel sürümle güncelle.").Accepts<AvailabilityInput>("application/json");
        DescribeEmpty(root.MapPost("/employees/{membershipId:guid}/availability/{availabilityId:guid}/archive", async (Guid businessId, Guid membershipId, Guid availabilityId, VersionInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, SchedulingValidation.Versioned, async x => { await s.ArchiveAvailability(Actor(p)!, businessId, membershipId, availabilityId, x, ct); return Results.NoContent(); })),
            "SchedulingArchiveAvailability", "Randevu kapsamını koruyarak uygunluğu arşivle.").Accepts<VersionInput>("application/json");

        Describe<AppointmentView>(root.MapPost("/appointments", async (Guid businessId, BookingInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, SchedulingValidation.Booking, async x =>
            {
                var result = await s.Create(Actor(p)!, businessId, x, ct);
                return result.Created ? Results.Created($"/api/v1/businesses/{businessId}/appointments/{result.Value.Id}", result.Value) : Results.Ok(result.Value);
            })), "SchedulingCreateAppointment", "Zorunlu, boş olmayan UUID clientRequestId ile oluştur; özdeş tekrar özgün yanıtı 200 ile döndürür.", 201)
            .Produces<AppointmentView>(200).Accepts<BookingInput>("application/json");
        Describe<AppointmentView>(root.MapGet("/appointments/{appointmentId:guid}", async (Guid businessId, Guid appointmentId, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            Results.Ok(await s.Detail(Actor(p)!, businessId, appointmentId, ct))), "SchedulingAppointmentDetail", "Randevunun güncel sürümünü ve ayrıntılarını getir.");
        Describe<AppointmentView[]>(root.MapGet("/appointments", async (Guid businessId, DateOnly fromDate, DateOnly toDateExclusive, int? page, int? pageSize, AppointmentStatus? status, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
        {
            var query = new CalendarInput(fromDate, toDateExclusive, page ?? 1, pageSize ?? 20, status);
            var errors = SchedulingValidation.Calendar(query);
            return errors.Count != 0 ? Results.ValidationProblem(errors) : Results.Ok(await s.Calendar(Actor(p)!, businessId, query, ct));
        }), "SchedulingCalendar", "İşletmenin güncel saat diliminde [fromDate,toDateExclusive) ile kesişen takvimi getir; 1–31 gün, sayfa boyutu 1–50.");
        Describe<AvailabilityCheckView>(root.MapPost("/appointments/availability-preview", async (Guid businessId, AvailabilityCheckInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, SchedulingValidation.AvailabilityCheck, async x => Results.Ok(await s.Preview(Actor(p)!, businessId, x, ct)))),
            "SchedulingAvailabilityPreview", "Anlık ve bağlayıcı olmayan uygunluğu kontrol et; rezervasyon oluşturmaz.").Accepts<AvailabilityCheckInput>("application/json");
        Describe<AppointmentView>(root.MapPost("/appointments/{appointmentId:guid}/reschedule", async (Guid businessId, Guid appointmentId, RescheduleInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, SchedulingValidation.Reschedule, async x => Results.Ok(await s.Reschedule(Actor(p)!, businessId, appointmentId, x, ct)))),
            "SchedulingRescheduleAppointment", "Randevuyu zorunlu güncel xmin sürümüyle atomik yeniden planla.").Accepts<RescheduleInput>("application/json");
        Describe<AppointmentView>(root.MapPost("/appointments/{appointmentId:guid}/cancel", async (Guid businessId, Guid appointmentId, VersionInput? input, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            await Body(input, SchedulingValidation.Versioned, async x => Results.Ok(await s.Cancel(Actor(p)!, businessId, appointmentId, x, ct)))),
            "SchedulingCancelAppointment", "Randevuyu güncel sürümle iptal et ve rezervasyonları atomik bırak.").Accepts<VersionInput>("application/json");
        Describe<EventView[]>(root.MapGet("/appointments/{appointmentId:guid}/events", async (Guid businessId, Guid appointmentId, ClaimsPrincipal p, ISchedulingService s, CancellationToken ct) =>
            Results.Ok((await s.Detail(Actor(p)!, businessId, appointmentId, ct)).Events)), "SchedulingAppointmentEvents", "Yetkili randevunun korunmuş, sıralı olay geçmişini getir.");
    }

    private static SchedulingActor? Actor(ClaimsPrincipal p) =>
        Guid.TryParse(p.FindFirstValue("sub"), out var user) && user != Guid.Empty && Guid.TryParse(p.FindFirstValue("sid"), out var session) && session != Guid.Empty
            ? new(user, session) : null;
    private static async Task<IResult> Body<T>(T? input, Func<T, Dictionary<string, string[]>> validate, Func<T, Task<IResult>> operation) where T : class
    {
        if (input == null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["JSON istek gövdesi gereklidir."] });
        var errors = validate(input);
        return errors.Count != 0 ? Results.ValidationProblem(errors) : await operation(input);
    }
    private static RouteHandlerBuilder Errors(RouteHandlerBuilder route, string name, string summary) => route.WithName(name).WithSummary(summary)
        .WithDescription("Geçerli JWT oturumu ve URL'deki işletmede güncel Owner yetkisi gereklidir. Alan/biçim hataları 400; bulunamayan tenant kaydı 404; durum, kaynak, idempotency ve sürüm çakışmaları 409. Ayrıntılar: docs/features/scheduling-api-phase9c.md.")
        .ProducesValidationProblem().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500);
    private static RouteHandlerBuilder Describe<T>(RouteHandlerBuilder route, string name, string summary, int status = 200) => Errors(route, name, summary).Produces<T>(status);
    private static RouteHandlerBuilder DescribeEmpty(RouteHandlerBuilder route, string name, string summary) => Errors(route, name, summary).Produces(204);
}
