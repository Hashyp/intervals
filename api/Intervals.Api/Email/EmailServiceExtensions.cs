using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Intervals.Api.Email;

public static class EmailServiceExtensions
{
    public static IServiceCollection AddIntervalsEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
