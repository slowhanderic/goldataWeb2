using Goldata.Infrastructure.Data;
using Goldata.Infrastructure.Identity;
using Goldata.Infrastructure.Legacy;
using Goldata.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Goldata.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGoldataInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DocumentOptions>(config.GetSection("Documents"));
        services.Configure<LegacyCompatOptions>(config.GetSection("LegacyCompat"));
        services.Configure<AuthOptions>(config.GetSection("Auth"));
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<Db>();
        services.AddSingleton<SchemaCache>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<UserContextService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<AdminService>();
        services.AddSingleton<RecordService>();
        services.AddSingleton<TodoService>();
        services.AddSingleton<LookupService>();
        services.AddSingleton<FileService>();
        return services;
    }
}
