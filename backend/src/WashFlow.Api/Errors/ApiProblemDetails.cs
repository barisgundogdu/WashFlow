using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WashFlow.Api.Errors;

public static class ApiProblemDetails
{
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;
        problem.Status = status;
        problem.Type = $"https://httpstatuses.com/{status}";
        problem.Title = status switch
        {
            400 => "İstek geçersiz.",
            401 => "Kimlik doğrulaması gerekli.",
            403 => "Bu işlem için yetkiniz yok.",
            404 => "Kaynak bulunamadı.",
            405 => "HTTP yöntemi desteklenmiyor.",
            409 => "İstek mevcut durumla çakışıyor.",
            415 => "İçerik türü desteklenmiyor.",
            429 => "Çok fazla istek gönderildi.",
            503 => "Hizmet şu anda kullanılamıyor.",
            _ => "İstek işlenemedi."
        };
        // Do not expose exception messages, request paths, query strings or framework details.
        problem.Detail = null;
        problem.Instance = null;
        problem.Extensions["code"] = status switch
        {
            400 => "validation", 401 => "unauthenticated", 403 => "forbidden",
            404 => "notFound", 405 => "methodNotAllowed", 409 => "conflict",
            415 => "unsupportedMediaType", 429 => "rateLimited",
            503 => "unavailable", _ => "unexpected"
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    }
}
