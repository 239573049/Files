﻿using File.Application.Contract;
using File.Application.Contract.Statistics.Dto;
using Microsoft.EntityFrameworkCore;

namespace File.Application;

public class StatisticsService : IStatisticsService
{
    private readonly FileDbContext _fileDbContext;

    public StatisticsService(FileDbContext fileDbContext)
    {
        _fileDbContext = fileDbContext;
    }

    /// <inheritdoc />
    public async Task<StatisticsDto> GetStatisticsAsync()
    {
        var statistics = new StatisticsDto();
        var currentTime = DateTime.Now;

        var todayStartTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 00:00:00"));
        var todayEndTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 23:59:59"));

        // 获取昨天时间
        var yesterdayStartTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 00:00:00")).AddDays(-1);
        var yesterdayEndTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 23:59:59")).AddDays(-1);
        statistics.Yesterday = await _fileDbContext
            .InterfaceStatistics
            .CountAsync(x => x.CreatedTime >= yesterdayStartTime && x.CreatedTime <= yesterdayEndTime);

        int week = Convert.ToInt32(currentTime.DayOfWeek);
        week = week == 0 ? 7 : week;
        var lastWeekStartTime = currentTime.AddDays(1 - week - 7);//上周星期一
        var lastWeekEndTime = currentTime.AddDays(7 - week - 7);//上周星期天

        statistics.LastWeek = await _fileDbContext
            .InterfaceStatistics
            .CountAsync(x => x.CreatedTime >= lastWeekStartTime && x.CreatedTime <= lastWeekEndTime);

        statistics.Today = await _fileDbContext.InterfaceStatistics.CountAsync(x => x.CreatedTime >= todayStartTime && x.CreatedTime <= todayEndTime);

        statistics.Total = await _fileDbContext.InterfaceStatistics.CountAsync();

        return statistics;
    }

    /// <inheritdoc />
    public async Task<List<PieDto>> GetPieAsync(PieInput input)
    {
        DateTime startTime;
        DateTime endTime;
        switch (input.Type)
        {
            case PieType.Today: // 获取今天开始时间和今天结束时间
                startTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 00:00:00"));
                endTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 23:59:59"));
                break;
            case PieType.Yesterday: // 今天减去一天就是昨天时间通过AddDays的方法去减
                startTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 00:00:00")).AddDays(-1);
                endTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 23:59:59")).AddDays(-1);
                break;
            case PieType.Month: // 获取本月初到现在的时间
                startTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-01 00:00:00"));
                endTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 23:59:59"));
                break;
            case PieType.Total: // 获取最大一年的时间
                startTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-01-01 00:00:00"));
                endTime = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd 23:59:59"));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        var data = _fileDbContext
            .InterfaceStatistics
            .Where(x => x.CreatedTime >= startTime && x.CreatedTime <= endTime)
            .GroupBy(x => x.Path)
            .Select(x => new PieDto
            {
                Type = x.Key,
                Value = x.Count()
            })
            .OrderByDescending(x => x.Value)
            .Skip(0).Take(20);

        return await data.ToListAsync();
    }

    /// <inheritdoc />
    public async Task<PagedResultDto<GetStatisticsDto>> GetListAsync(GetStatisticsInput input)
    {
        var query = _fileDbContext
            .InterfaceStatistics
            .Where(x =>
            string.IsNullOrEmpty(input.Keywords) || x.Path.Contains(input.Keywords) || x.Query.Contains(input.Keywords))
            .OrderByDescending(x => x.CreatedTime);

        var data = await query
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToListAsync();

        var count = await query.CountAsync();

        return new PagedResultDto<GetStatisticsDto>(count, data.Select(x => new GetStatisticsDto(x.Id, x.Code, x.Succeed, x.ResponseTime, x.Path, x.CreatedTime, x.UserId, x.Query)));
    }
}