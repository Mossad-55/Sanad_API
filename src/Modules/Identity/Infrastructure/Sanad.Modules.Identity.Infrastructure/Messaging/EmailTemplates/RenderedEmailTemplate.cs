namespace Sanad.Modules.Identity.Infrastructure.Messaging.EmailTemplates;

public sealed record RenderedEmailTemplate(string Subject, string HtmlBody, string PlainTextBody);
