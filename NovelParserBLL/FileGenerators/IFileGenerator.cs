﻿using NovelParserBLL.Models;

namespace NovelParserBLL.FileGenerators
{
    public enum FileFormatForGenerator
    {
        EPUB,
        PDF,
    }

    internal interface IFileGenerator
    {
        public Task Generate(string file, Novel novel, SortedList<int, Chapter> chapters);
    }
}
