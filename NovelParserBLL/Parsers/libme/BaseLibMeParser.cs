﻿using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jint;
using Newtonsoft.Json;
using NovelParserBLL.JsDTO;
using NovelParserBLL.Models;
using NovelParserBLL.Properties;
using NovelParserBLL.Services;
using NovelParserBLL.Utilities;

namespace NovelParserBLL.Parsers.Libme;

internal abstract class BaseLibMeParser : INovelParser
{
    private const string InfoScriptStartPattern = @"(?i)^window\.__DATA__(?-i)";
    private readonly HttpClient _client;
    protected readonly SetProgress setProgress;
    
    protected BaseLibMeParser(SetProgress setProgress, HttpClient client)
    {
        this.setProgress = setProgress;
        _client = client;
    }

    public abstract string SiteDomain { get; }
    public abstract string SiteName { get; }
    public ParserInfo ParserInfo => new (SiteDomain, SiteName, "https://lib.social/login");
    public abstract Task LoadChapters(Novel novel, string group, string pattern, bool includeImages, CancellationToken token);
    public async Task<Novel> ParseCommonInfo(Novel novel, CancellationToken token)
    {
        setProgress(0, 0, Resources.ProgressStatusLoading);

        if (!Directory.Exists(novel.DownloadFolderName))
            Directory.CreateDirectory(novel.DownloadFolderName);

        if (string.IsNullOrEmpty(novel.URL)) return novel;

        var content = await GetPageContent(novel.URL, token);
        var htmlDoc = await GetHtmlDocument(content, token);
        if (htmlDoc == null) return novel;

        var infoScript = GetInfoScript(htmlDoc);
        if (string.IsNullOrEmpty(infoScript)) return novel;

        var novelInfo = GetNovelInfo(infoScript);
        if (novelInfo == null) return novel;

        var tempNovel = new Novel
        {
            Name = GetNovelName(novelInfo),
            Author = GetNovelAuthor(htmlDoc),
            Description = GetNovelDescription(htmlDoc)
        };

        if (!(novel.Cover?.Exists ?? false))
        {
            var coverUrl = GetNovelCoverUrl(htmlDoc, novelInfo.manga.name);
            tempNovel.Cover = novel.Cover ?? new ImageInfo(novel.DownloadFolderName, coverUrl);
            await DownloadUrl(tempNovel.Cover.URL, tempNovel.Cover.FullPath);
        }

        tempNovel.ChaptersByGroup = GetChapters(novelInfo);
        novel.Merge(tempNovel);
        novel.Cover = FileHelper.UpdateImageInfo(novel.Cover, novel.DownloadFolderName);

        return novel;
    }
    public abstract string PrepareUrl(string url);
    public abstract bool ValidateUrl(string url);

    protected async Task<string> GetPageContent(string url, CancellationToken token = default)
    {
        var response = await _client.GetAsync(url, token);
        if (!response.IsSuccessStatusCode) 
            return string.Empty;

        var content = await response.Content.ReadAsStringAsync(token);
        return content;
    }
    protected async Task DownloadUrl(string url, string fullPath)
    {
        var content = await _client.GetByteArrayAsync(url);
        await using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
        await fs.WriteAsync(content);
        await fs.FlushAsync();
    }

    protected static async Task<IHtmlDocument?> GetHtmlDocument(string content, CancellationToken token = default)
    {
        var parser = new HtmlParser();
        return await parser.ParseDocumentAsync(content, token);
    }
    private static string GetInfoScript(IParentNode doc)
    {
        var scripts = doc.QuerySelectorAll("script");

        return scripts.FirstOrDefault(s => 
                Regex.IsMatch(s.InnerHtml.Trim(), InfoScriptStartPattern))?
            .InnerHtml.Trim() 
               ?? string.Empty;
    }
    private static JsNovelInfo? GetNovelInfo(string contentScript)
    {
        var jsEngine = new Engine();
        jsEngine.Execute("var window = {__DATA__:{}};");
        jsEngine.Execute(contentScript);
        jsEngine.Execute("var jsonData = JSON.stringify(window.__DATA__);");
        var novelInfoJson = jsEngine.GetValue("jsonData").AsString();
        
        return string.IsNullOrEmpty(novelInfoJson) 
            ? null 
            : JsonConvert.DeserializeObject<JsNovelInfo>(novelInfoJson);
    }
    private static string GetNovelName(JsNovelInfo info)
    {
        var name = info.manga.engName;
        if (string.IsNullOrWhiteSpace(name)) name = info.manga.rusName;
        if (string.IsNullOrWhiteSpace(name)) name = info.manga.slug;
        return name;
    }
    private static string GetNovelAuthor(IParentNode doc)
    {
        var elems = doc.QuerySelectorAll("div.media-info-list__item");
        var result = elems.FirstOrDefault(e=>string.Equals(e.Children[0].InnerHtml, "Автор", 
                             StringComparison.OrdinalIgnoreCase))
                         ?.Children[1]
                         .Children[0]
                         .InnerHtml
                         .Trim()
                     ?? "(Неизвестно)";
        return result;
    }
    private static string GetNovelDescription(IParentNode doc)
    {
        var elem = doc.QuerySelector(".media-description__text");
        return elem?.Text().Trim() ?? "(Нет описания)";
    }
    private static string GetNovelCoverUrl(IParentNode doc, string title)
    {
        var image = (doc.QuerySelector(@$"img[alt=""{title}""]") 
                    ?? doc.QuerySelector("img.media-header__cover")
                    ?? doc.QuerySelector("div.media-sidebar__cover.paper>img"))
                    as IHtmlImageElement;
        return image?.Source ?? string.Empty;
    }
    private static Dictionary<string, SortedList<float, Chapter>> GetChapters(JsNovelInfo jsInfo)
    {
        var result = new Dictionary<string, SortedList<float, Chapter>>();

        var branches = jsInfo.chapters.branches.Length > 0
            ? jsInfo.chapters.branches
            : new JsBranch[] { new() { id = "nobranches", name = "none" } };

        var slug = jsInfo.manga.slug;

        foreach (var branch in branches)
        {
            var chapters = jsInfo.chapters.list.Where(ch => ch.branch_id == branch.id || branch.id == "nobranches");
            var chaptersList = new SortedList<float, Chapter>();
            foreach (var chapter in chapters)
            {
                var chapInfo = new Chapter
                {
                    Name = chapter.chapter_name,
                    Number = chapter.chapter_number,
                    Url = $@"https://ranobelib.me/{slug}/v{chapter.chapter_volume}/c{chapter.chapter_number}"
                };
                //todo Что-то сделать с именованием глав...
                var chapNumber = float.Parse(chapter.chapter_number, CultureInfo.InvariantCulture.NumberFormat);
                while (chaptersList.ContainsKey(chapNumber))
                {
                    chapNumber += 0.01f;
                }
                chaptersList.Add(chapNumber, chapInfo);
            }
            result.Add(branch.id, chaptersList);
        }

        return result;
    }

    
}