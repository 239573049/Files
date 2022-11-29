using File.Application.Contract.Base;
using File.Application.Contract.Directorys;
using File.Application.Contract.Files;
using File.Application.Contract.Files.Input;
using File.Application.Contract.Options;
using File.Application.Contract.RouteMappings;
using File.Application.Contract.RouteMappings.Input;
using File.Application.Contract.UserInfos;
using File.Application.Contract.UserInfos.Input;
using File.Application.Extensions;
using File.HttpApi.Host.Filters;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using File.Application.Contract.Statistics;
using File.Application.Contract.Statistics.Input;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Features;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddMvcCore(options => { options.Filters.Add<ResultFilter>(); });

    // 跨域策略
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("CorsPolicy", corsBuilder =>
        {
            corsBuilder.SetIsOriginAllowed((string _) => true).AllowAnyMethod().AllowAnyHeader()
                .AllowCredentials();
        });
    });

    builder.Services.AddFileApplication(builder.Configuration.GetConnectionString("Default")!);

    var configurationSection = builder.Configuration.GetSection(nameof(TokenOptions));

    builder.Services.Configure<TokenOptions>(configurationSection);

    builder.Services.AddHttpContextAccessor();

    builder.Services.AddJwt(configurationSection.Get<TokenOptions>()!);

    var app = builder.Build();

    // 异常处理中间件
    app.Use(async (content, next) =>
    {
        try
        {
            await next.Invoke(content);
        }
        catch (BusinessException business)
        {
            content.Response.StatusCode = business.Code;
            await content.Response.Body.WriteAsync(
                JsonSerializer.SerializeToUtf8Bytes(new HttpResultDto(business.Message, null, business.Code)));
        }
        catch (UnauthorizedAccessException)
        {
            content.Response.StatusCode = 500;
            await content.Response.Body.WriteAsync(
                JsonSerializer.SerializeToUtf8Bytes(new HttpResultDto("您没有权限操作", null, 500)));
        }
        catch (NotSupportedException)
        {
            content.Response.StatusCode = 500;
            await content.Response.Body.WriteAsync(
                JsonSerializer.SerializeToUtf8Bytes(new HttpResultDto("路径格式错误", null, 500)));
        }
        catch (DirectoryNotFoundException)
        {
            content.Response.StatusCode = 500;
            await content.Response.Body.WriteAsync(
                JsonSerializer.SerializeToUtf8Bytes(new HttpResultDto("路径无效", null, 500)));
        }
        catch (Exception ex)
        {
            content.Response.StatusCode = 500;
            await content.Response.Body.WriteAsync(
                JsonSerializer.SerializeToUtf8Bytes(new HttpResultDto(ex.Message, null, 500)));
        }

    });

    // 注意使用顺序防止错误
    app.UseFileApplication();

    app.UseStaticFiles();

    #region file

    app.MapGet("/api/file/list", (IFileService fileService, string? name, string? path, int? page, int? pageSize)
        => fileService.GetListAsync(new GetListInput(name, path, page, pageSize)))
        .RequireAuthorization();

    app.MapGet("/api/file/content", (IFileService fileService, string filePath)
        => fileService.GetFileContentAsync(filePath))
        .RequireAuthorization();

    app.MapPost("/api/file/save", (IFileService fileService, SaveFileContentInput input)
        => fileService.SaveFileContentAsync(input))
        .RequireAuthorization();

    app.MapDelete("/api/file", (IFileService fileService, string path)
        => fileService.DeleteFileAsync(path))
        .RequireAuthorization();

    app.MapPost("/api/file", (IFileService fileService, CreateFileInput input)
        => fileService.CreateAsync(input))
        .RequireAuthorization();

    app.MapPost("/api/file/extract-directory", (IFileService fileService, string path, string name)
        => fileService.ExtractToDirectoryAsync(path, name))
        .RequireAuthorization();

    app.MapPost("/api/file/uploading", async (string path, string name, [FromForm] IFormFile file) =>
    {
        try
        {
            var p = Path.Combine(path, name.TrimEnd(file.FileName.ToCharArray()));
            if (!Directory.Exists(p))
            {
                Directory.CreateDirectory(p);
            }
            await using var fileStream = System.IO.File.Open(Path.Combine(p, file.FileName), FileMode.Create, FileAccess.Write);
            await file.CopyToAsync(fileStream);
            fileStream.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }).RequireAuthorization();

    #endregion

    #region directory

    app.MapDelete("/api/directory", (IDirectoryService directoryService, string path)
        => directoryService.DeleteAsync(path))
        .RequireAuthorization();

    app.MapPost("/api/directory", (IDirectoryService directoryService, string path, string name)
        => directoryService.CreateAsync(path, name))
        .RequireAuthorization();

    app.MapPut("/api/directory/rename", (IDirectoryService directoryService, string fullName, string path, string name)
        => directoryService.RenameAsync(fullName, path, name))
        .RequireAuthorization();

    #endregion

    #region routeMapping

    app.MapPost("/api/route-mapping", (IRouteMappingService routeMappingService, CreateRouteMappingInput input)
        => routeMappingService.CreateAsync(input))
        .RequireAuthorization();

    app.MapDelete("/api/route-mapping", (IRouteMappingService routeMappingService, string route)
        => routeMappingService.DeleteAsync(route))
        .RequireAuthorization();

    app.MapGet("/api/route-mapping", (IRouteMappingService routeMappingService, string path)
        => routeMappingService.GetAsync(path))
        .RequireAuthorization();


    #endregion

    #region auth

    app.MapPost("/api/auth",
        async (IUserInfoService userInfoService, AuthInput input, IOptions<TokenOptions> tokenOptions)
            =>
        {
            var userInfo = await userInfoService.AuthAsync(input);

            var claims = new[]
            {
                new Claim("userInfo", JsonSerializer.Serialize(userInfo)),
                new Claim("Id", userInfo.Id.ToString())
            };

            var cred = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenOptions.Value.SecretKey)),
                SecurityAlgorithms.HmacSha256);

            var jwtSecurityToken = new JwtSecurityToken(
                tokenOptions.Value.Issuer, // 签发者
                tokenOptions.Value.Audience, // 接收者
                claims, // payload
                expires: DateTime.Now.AddHours(tokenOptions.Value.ExpireHours), // 过期时间
                signingCredentials: cred); // 令牌
            return new JwtSecurityTokenHandler().WriteToken(jwtSecurityToken);
        });

    app.MapGet("/api/user-info", (IUserInfoService userInfoService)
        => userInfoService.GetAsync())
        .RequireAuthorization(); ;

    #endregion

    #region statistics

    app.MapGet("/api/statistics/statistics", (IStatisticsService statisticsService)
        => statisticsService.GetStatisticsAsync())
        .RequireAuthorization(); ;

    app.MapGet("/api/statistics/pie", (IStatisticsService statisticsService, PieType type)
        => statisticsService.GetPieAsync(new PieInput() { Type = type }))
        .RequireAuthorization(); ;

    #endregion

    app.UseCors("CorsPolicy");

    await app.RunAsync();
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync(exception.Message);
}
