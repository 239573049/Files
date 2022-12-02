﻿using File.Application.Contract.Base;

namespace File.Application.Contract.Statistics.Input;

public class GetStatisticsInput : PagedRequestInput
{
    /// <summary>
    /// 关键字
    /// </summary>
    public string Keywords { get; set; }

    public GetStatisticsInput(string keywords, int page = 1, int pageSize = 20)
    {
        Keywords = keywords;
        Page = page;
        PageSize = pageSize;
    }
}