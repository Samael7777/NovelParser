﻿using NovelParserBLL.Models;

namespace NovelParserBLL.Parsers
{
    internal interface INovelParser
    {
        Task LoadChapters(Novel novel, string group, string pattern , bool includeImages, CancellationToken cancellationToken);
        Task<Novel> ParseCommonInfo(Novel novel, CancellationToken cancellationToken);
        bool ValidateUrl(string url);
        string PrepareUrl(string url);
    }
}