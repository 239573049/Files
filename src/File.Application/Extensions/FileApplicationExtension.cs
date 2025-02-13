﻿using File.Application.Contract;
using File.Application.Contract.Eto;
using File.Entity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.Text;
using Token.Extensions;
using Token.Handlers;

namespace File.Application.Extensions;

public static class FileApplicationExtension
{
    public static async void AddFileApplication(this IServiceCollection services, string connectString)
    {
        services.AddMemoryCache();
        services.AddTransient<IFileService, FileService>();
        services.AddTransient<IDirectoryService, DirectoryService>();
        services.AddTransient<IRouteMappingService, RouteMappingService>();
        services.AddTransient<IUserInfoService, UserInfoService>();
        services.AddTransient<IStatisticsService, StatisticsService>();
        services.AddTransient<CurrentManage>();
        services.AddTransient<CurrentLimitingMiddleware>();

        services.AddDbContext<FileDbContext>(options =>
        {
            options.UseSqlite(connectString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
        });

        services.AddSingleton((service) =>
        {
            var options = new StatisticsOptions();
            options.IgnoreUri.Add("/api/statistics/pie");
            options.IgnoreUri.Add("/api/statistics/statistics");
            options.IgnoreUri.Add("/api/file/list");
            options.IgnoreUri.Add("/api/auth");
            options.IgnoreUri.Add("/api/statistics/list");
            options.IgnoreUri.Add("/uploading");
            return options;
        });

        await services.AddRouteMapping();

        // 注入本地事件服务
        services.AddEventBus();
        services.AddTransient<FileMiddleware>();

        services.AddTransient(typeof(ILoadEventHandler<InterfaceStatisticsEto>),
            typeof(InterfaceStatisticsEventHandle));
        services.AddTransient(typeof(ILoadEventHandler<DeleteFileEto>),
            typeof(DeleteFileHandle));

        services.AddTransient<InterfaceStatisticsMiddleware>();
    }

    public static void AddJwt(this IServiceCollection services, TokenOptions tokenOption)
    {
        services.AddAuthorization();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, //是否在令牌期间验证签发者
                    ValidateAudience = true, //是否验证接收者
                    ValidateLifetime = true, //是否验证失效时间
                    ValidateIssuerSigningKey = true, //是否验证签名
                    ValidAudience = tokenOption.Audience, //接收者
                    ValidIssuer = tokenOption.Issuer, //签发者，签发的Token的人
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenOption.SecretKey!)) // 密钥
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = (context) =>
                    {
                        // 添加signalr的token 因为signalr的token在请求头上所以需要设置
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.Value.StartsWith("/uploading"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });
    }

    /// <summary>
    /// 加载缓存
    /// </summary>
    /// <param name="services"></param>
    private static async Task AddRouteMapping(this IServiceCollection services)
    {
        var mappings = new ConcurrentDictionary<string, RouteMapping>();
        try
        {
            var dbContext = services.BuildServiceProvider().GetService<FileDbContext>();
            var routes = new List<RouteMapping>();
            routes.AddRange(await dbContext!.RouteMappings.ToListAsync());
            routes.ForEach(x => { mappings.TryAdd(x.Route, x); });
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.Message);
        }

        services.AddSingleton(mappings);
    }

    /// <summary>
    /// 使用FileApplication
    /// </summary>
    /// <param name="app"></param>
    public static void UseFileApplication(this IApplicationBuilder app)
    {
        app.UseMiddleware<CurrentLimitingMiddleware>();
        app.UseMiddleware<InterfaceStatisticsMiddleware>();
        app.UseMiddleware<FileMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
    }
}