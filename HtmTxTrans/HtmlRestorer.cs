using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace HtmTxTrans;

public class HtmlRestorer
{
    private Dictionary<int, string> _translatedMap = new Dictionary<int, string>();
    private readonly IDeserializer _yamlDeserializer;

    // 定義不需要 Closing Tag 的空元素 (Void Elements)
    private readonly HashSet<string> _voidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "br", "img", "hr", "meta", "input", "link"
    };

    public HtmlRestorer()
    {
        // 建立 YAML 反序列化器
        _yamlDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public void Restore(string originalHtmlPath, string workingDir, string outputFileName)
    {
        LoadTranslatedData(workingDir);

        // 使用 ProjectFiles 取得路徑
        var holds = LoadHoldData(ProjectFiles.Hold(workingDir));
        var metadata = ProjectMetadata.LoadMetadata(ProjectFiles.Metadata(workingDir));
        int attributeBoundary = metadata?.AttributeIdBoundary ?? 0;

        // 1. 替換 <x_n> 為實際 HTML，並執行 Stack Check 修復
        ProcessTranslatedText(holds);

        // 2. 讀取 Pass 1 預留的 Skeleton HTML 作為字串基底
        string skeletonPath = ProjectFiles.Skeleton(workingDir);
        if (!File.Exists(skeletonPath))
        {
            SimpleLogger.LogCustom($"[Pass 6] Error - Cannot find skeleton file: {skeletonPath}", ConsoleColor.Red);
            return;
        }

        string skeletonHtml = File.ReadAllText(skeletonPath);

        Console.Write("Working...");
        int processedCount = 0;

        var nodeIds = _translatedMap.Keys.Where(id => metadata == null || id >= attributeBoundary).ToList();
        var attributeIds = _translatedMap.Keys.Where(id => metadata != null && id < attributeBoundary).ToList();

        // 3-1. 第一階段：先還原所有一般文字節點
        foreach (var id in nodeIds)
        {
            string safeTranslatedHtml = _translatedMap[id];
            string nodePattern = $@"<HtmTxT\s+id=""{id}"">.*?</HtmTxT>";
            skeletonHtml = Regex.Replace(skeletonHtml, nodePattern, safeTranslatedHtml, RegexOptions.IgnoreCase);

            if (metadata == null)
            {
                string encodedAttrValue = System.Net.WebUtility.HtmlEncode(safeTranslatedHtml);
                skeletonHtml = Regex.Replace(skeletonHtml, $@"\[\[htmtxt_ID_{id}\]\]", encodedAttrValue, RegexOptions.IgnoreCase);
            }

            processedCount++;
            if (processedCount % 200 == 0) Console.Write(".");
        }

        // 3-2. 第二階段：還原所有屬性
        foreach (var id in attributeIds)
        {
            string safeTranslatedHtml = _translatedMap[id];
            string encodedAttrValue = System.Net.WebUtility.HtmlEncode(safeTranslatedHtml);
            string attrPattern = $@"\[\[htmtxt_ID_{id}\]\]";

            skeletonHtml = Regex.Replace(skeletonHtml, attrPattern, encodedAttrValue, RegexOptions.IgnoreCase);

            processedCount++;
            if (processedCount % 200 == 0) Console.Write(".");
        }

        Console.WriteLine("done.");

        // 4. 直接寫入最終檔案
        string outputPath = Path.Combine(workingDir, outputFileName);
        File.WriteAllText(outputPath, skeletonHtml);

        SimpleLogger.LogCustom($"File saved to: {outputPath}");
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 6] HTML Restoration complete ", 60, '='));
    }

    private void LoadTranslatedData(string workingDir)
    {
        _translatedMap.Clear();
        // 讀取所有的 Final.*.yaml 檔案
        var files = Directory.GetFiles(workingDir, "Final.*.yaml");
        foreach (var file in files)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var nodes = _yamlDeserializer.Deserialize<List<NodeEntry>>(yaml);
                if (nodes != null)
                {
                    foreach (var node in nodes) _translatedMap[node.Id] = node.Text;
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.LogCustom($"[Pass 6] Error reading {Path.GetFileName(file)}: {ex.Message}", ConsoleColor.Red);
            }
        }
    }

    private Dictionary<string, string> LoadHoldData(string holdPath)
    {
        if (!File.Exists(holdPath)) return new Dictionary<string, string>();
        try
        {
            var yaml = File.ReadAllText(holdPath);
            var holds = _yamlDeserializer.Deserialize<List<HoldEntry>>(yaml);
            return holds?.ToDictionary(h => h.Placeholder, h => h.Content) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void ProcessTranslatedText(Dictionary<string, string> holds)
    {
        var tagRegex = new Regex(@"<x_\d+>", RegexOptions.Compiled);
        var keys = _translatedMap.Keys.ToList();

        foreach (var id in keys)
        {
            string translatedText = _translatedMap[id];
            string rawHtml = tagRegex.Replace(translatedText, match => { return holds.TryGetValue(match.Value, out var content) ? content : match.Value; });
            _translatedMap[id] = FixHtmlTags(rawHtml);
        }
    }

    private string FixHtmlTags(string html)
    {
        var matches = Regex.Matches(html, @"<\s*(/?)\s*([a-zA-Z0-9]+)[^>]*>");
        var openStack = new List<(string Name, Match M)>();
        var keepMatches = new HashSet<Match>();

        foreach (Match m in matches)
        {
            bool isClosing = m.Groups[1].Value == "/";
            string tagName = m.Groups[2].Value.ToLower();

            if (_voidElements.Contains(tagName) || m.Value.EndsWith("/>")) { keepMatches.Add(m); continue; }

            if (!isClosing) { openStack.Add((tagName, m)); }
            else
            {
                int matchIndex = -1;
                for (int i = openStack.Count - 1; i >= 0; i--) { if (openStack[i].Name == tagName) { matchIndex = i; break; } }
                if (matchIndex != -1) { keepMatches.Add(openStack[matchIndex].M); keepMatches.Add(m); openStack.RemoveAt(matchIndex); }
            }
        }

        var sb = new StringBuilder();
        int lastIndex = 0;
        foreach (Match m in matches)
        {
            sb.Append(html.Substring(lastIndex, m.Index - lastIndex));
            if (keepMatches.Contains(m)) sb.Append(m.Value);
            lastIndex = m.Index + m.Length;
        }
        sb.Append(html.Substring(lastIndex));

        return sb.ToString();
    }
}