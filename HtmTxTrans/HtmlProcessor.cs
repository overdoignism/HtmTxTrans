using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace HtmTxTrans;

public class HtmlProcessor
{
    private readonly List<string> _inlineTags = new List<string> { "b", "i", "a", "br", "img", "strong", "em", "u", "small", "mark", "sub", "sup", "abbr", "time", "q", "font", "center" };
    private readonly List<string> _ignoreTags = new List<string> { "script", "style", "meta", "iframe", "pre" };
    private readonly string[] _opaqueInlineTags = { "code", "kbd", "var", "samp" };

    private int _nodeCounter = 0;
    private int _tagCounter = 0;

    private StringBuilder _textBuffer = new StringBuilder();
    private List<INode> _currentBufferNodes = new List<INode>();

    private readonly AppConfig _config;
    private readonly LlmPromptConfig? _promptConfig;
    private readonly LlmService? _llmService;
    private readonly bool _useLlmBoundary;
    private readonly bool _disableSepResolution;
    private readonly bool _translatePreTags;

    // 建立全域的 YAML 序列化器
    private readonly ISerializer _yamlSerializer;

    public HtmlProcessor(AppConfig config, LlmPromptConfig? promptConfig = null, LlmService? llmService = null, bool useLlmBoundary = false, bool disableSepResolution = false, bool translatePreTags = false)
    {
        _config = config ?? new AppConfig();
        _translatePreTags = translatePreTags;
        if (_translatePreTags) { _ignoreTags.Remove("pre"); }

        _promptConfig = promptConfig;
        _llmService = llmService;
        _useLlmBoundary = useLlmBoundary;
        _disableSepResolution = disableSepResolution;

        _yamlSerializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
    }

    public HtmlProcessor()
    {
        _config = new AppConfig();
        _yamlSerializer = new SerializerBuilder().Build();
    }

    public async Task ExtractAsync(string inputPath, string workingDir, int windowSize)
    {
        _nodeCounter = 0;
        _tagCounter = 0;
        _textBuffer.Clear();
        _currentBufferNodes.Clear();

        var htmlContent = await File.ReadAllTextAsync(inputPath);
        var parser = new HtmlParser();
        var document = parser.ParseDocument(htmlContent);

        var pre0Nodes = new List<NodeEntry>();
        var pre0Holds = new List<HoldEntry>();

        int attributeIdBoundary = ExtractAllTargetAttributes(document, pre0Nodes);

        if (document.Body != null)
        {
            TraverseNodes(document.Body, pre0Nodes, pre0Holds);
            FlushBuffer(document.Body, pre0Nodes);
        }

        // 使用統一的路徑管理器
        string skeletonPath = ProjectFiles.Skeleton(workingDir);
        await File.WriteAllTextAsync(skeletonPath, document.ToHtml());

        var mergeResult = MergeTags(pre0Nodes, pre0Holds);
        List<NodeEntry> finalNodes = mergeResult.Nodes;
        List<HoldEntry> finalHolds = mergeResult.Holds;
        Console.WriteLine("[Pass 1] Consecutive tags merged.");

        // 儲存全節點與 Hold 表 (轉為 YAML)
        SaveYaml(ProjectFiles.NodeAll(workingDir), finalNodes);
        SaveYaml(ProjectFiles.Hold(workingDir), finalHolds);

        int totalFiles = await SaveAsYamlChunksAsync(finalNodes, workingDir, windowSize, attributeIdBoundary);

        var metadata = new ProjectMetadata
        {
            AttributeIdBoundary = attributeIdBoundary,
            TotalIdsUpper = finalNodes.Count - 1,
            TotalFilesUpper = totalFiles
        };
        SaveYaml(ProjectFiles.Metadata(workingDir), metadata);

        SimpleLogger.LogCustom((Utitilty.PadCenterCustom(" [Pass 1] HTML Extraction finish ", 60, '=')));
        Console.WriteLine("");
    }

    private void TraverseNodes(INode parent, List<NodeEntry> allNodes, List<HoldEntry> holdEntries)
    { /* ... 不變 ... */
        var children = parent.ChildNodes.ToList();
        foreach (var node in children)
        {
            if (node is IElement element)
            {
                string localName = element.LocalName.ToLower();
                if (_ignoreTags.Contains(localName)) continue;
                if (_opaqueInlineTags.Contains(localName))
                {
                    _currentBufferNodes.Add(node);
                    string placeholder = $"<x_{_tagCounter++}>";
                    holdEntries.Add(new HoldEntry { Placeholder = placeholder, Content = element.OuterHtml });
                    _textBuffer.Append(placeholder); continue;
                }
                if (_inlineTags.Contains(localName))
                {
                    _currentBufferNodes.Add(node);
                    string openTag = $"<x_{_tagCounter++}>";
                    holdEntries.Add(new HoldEntry { Placeholder = openTag, Content = GetFullOpenTag(element) });
                    _textBuffer.Append(openTag);
                    ExtractInlineContent(node, holdEntries);
                    string closeTag = $"<x_{_tagCounter++}>";
                    holdEntries.Add(new HoldEntry { Placeholder = closeTag, Content = $"</{element.LocalName}>" });
                    _textBuffer.Append(closeTag); continue;
                }
            }
            if (node.NodeType == NodeType.Text)
            {
                _textBuffer.Append(node.TextContent); _currentBufferNodes.Add(node);
            }
            else
            {
                FlushBuffer(parent, allNodes); TraverseNodes(node, allNodes, holdEntries); FlushBuffer(parent, allNodes);
            }
        }
        FlushBuffer(parent, allNodes);
    }

    private void ExtractInlineContent(INode parent, List<HoldEntry> holdEntries)
    { /* ... 不變 ... */
        foreach (var node in parent.ChildNodes)
        {
            if (node.NodeType == NodeType.Text) { _textBuffer.Append(node.TextContent); }
            else if (node is IElement el)
            {
                string localName = el.LocalName.ToLower();
                if (_opaqueInlineTags.Contains(localName) || _ignoreTags.Contains(localName))
                {
                    string placeholder = $"<x_{_tagCounter++}>";
                    holdEntries.Add(new HoldEntry { Placeholder = placeholder, Content = el.OuterHtml });
                    _textBuffer.Append(placeholder);
                }
                else
                {
                    string openTag = $"<x_{_tagCounter++}>";
                    holdEntries.Add(new HoldEntry { Placeholder = openTag, Content = GetFullOpenTag(el) });
                    _textBuffer.Append(openTag);
                    ExtractInlineContent(node, holdEntries);
                    string closeTag = $"<x_{_tagCounter++}>";
                    holdEntries.Add(new HoldEntry { Placeholder = closeTag, Content = $"</{el.LocalName}>" });
                    _textBuffer.Append(closeTag);
                }
            }
            else { ExtractInlineContent(node, holdEntries); }
        }
    }

    private void FlushBuffer(INode parent, List<NodeEntry> allNodes)
    { /* ... 不變 ... */
        if (_currentBufferNodes.Count == 0) return;
        var normalizedText = Regex.Replace(_textBuffer.ToString(), @"\s+", " ").Trim();
        if (!string.IsNullOrEmpty(normalizedText))
        {
            int currentId = _nodeCounter++;
            allNodes.Add(new NodeEntry { Id = currentId, Text = normalizedText });
            if (parent.Owner != null)
            {
                var skeletonTag = parent.Owner.CreateElement("HtmTxT");
                skeletonTag.SetAttribute("id", currentId.ToString());
                skeletonTag.TextContent = $"ID_{currentId}";
                var firstNode = _currentBufferNodes[0];
                parent.InsertBefore(skeletonTag, firstNode);
                foreach (var oldNode in _currentBufferNodes) parent.RemoveChild(oldNode);
            }
        }
        _textBuffer.Clear(); _currentBufferNodes.Clear();
    }

    private class MergeResult { public List<NodeEntry> Nodes { get; set; } = new List<NodeEntry>(); public List<HoldEntry> Holds { get; set; } = new List<HoldEntry>(); }

    private MergeResult MergeTags(List<NodeEntry> pre0Nodes, List<HoldEntry> pre0Holds)
    { /* ... 不變 ... */
        var holdDict = pre0Holds.ToDictionary(h => h.Placeholder, h => h.Content);
        var pre1Nodes = new List<NodeEntry>(); var pre1Holds = new List<HoldEntry>(); int newTagCounter = 0;
        var consecutiveTagsRegex = new Regex(@"(?:<x_\d+>)+", RegexOptions.Compiled);
        var singleTagRegex = new Regex(@"<x_\d+>", RegexOptions.Compiled);

        foreach (var node in pre0Nodes)
        {
            string mergedText = consecutiveTagsRegex.Replace(node.Text, match => {
                var individualTags = singleTagRegex.Matches(match.Value);
                var combinedContent = new StringBuilder();
                foreach (Match m in individualTags) if (holdDict.TryGetValue(m.Value, out string? content)) combinedContent.Append(content);
                string newPlaceholder = $"<x_{newTagCounter++}>";
                pre1Holds.Add(new HoldEntry { Placeholder = newPlaceholder, Content = combinedContent.ToString() });
                return newPlaceholder;
            });
            pre1Nodes.Add(new NodeEntry { Id = node.Id, Text = mergedText });
        }
        return new MergeResult { Nodes = pre1Nodes, Holds = pre1Holds };
    }

    // 參數移除了 baseName
    private async Task<int> SaveAsYamlChunksAsync(List<NodeEntry> nodes, string workingDir, int windowSize, int attributeIdBoundary)
    {
        int chunkIndex = 0; int currentEstimatedSize = 0; var currentChunk = new List<NodeEntry>();
        int lastBoundaryIndex_1st = -1; int lastBoundaryIndex_2nd = -1; const int yamlOverheadPerNode = 15;
        int hardLimit = (int)(windowSize * _config.SlidingWindowHardScale);

        foreach (var node in nodes)
        {
            if (attributeIdBoundary > 0 && node.Id == attributeIdBoundary && currentChunk.Any())
            {
                SimpleLogger.LogCustom($"[Pass 1] Attribute boundary reached. Forced packaging - " + GetChunkLogMessage(currentChunk, chunkIndex), ConsoleColor.Cyan);
                await WriteChunkAsync(currentChunk, workingDir, chunkIndex++, attributeIdBoundary);
                currentChunk.Clear(); currentEstimatedSize = 0; lastBoundaryIndex_1st = -1; lastBoundaryIndex_2nd = -1;
            }

            currentChunk.Add(node);
            currentEstimatedSize += node.Text.Length + yamlOverheadPerNode;
            if (IsSentenceBoundary_1st(node.Text)) lastBoundaryIndex_1st = currentChunk.Count - 1;
            if (IsSentenceBoundary_2nd(node.Text)) lastBoundaryIndex_2nd = currentChunk.Count - 1;

            if (currentEstimatedSize >= windowSize)
            {
                if (lastBoundaryIndex_1st != -1 && lastBoundaryIndex_1st < currentChunk.Count - 1)
                {
                    var readyChunk = currentChunk.Take(lastBoundaryIndex_1st + 1).ToList();
                    SimpleLogger.LogCustom($"[Pass 1] Soft limit - " + GetChunkLogMessage(readyChunk, chunkIndex), ConsoleColor.White);
                    await WriteChunkAsync(readyChunk, workingDir, chunkIndex++, attributeIdBoundary);
                    var overflowNodes = currentChunk.Skip(lastBoundaryIndex_1st + 1).ToList();
                    currentChunk.Clear(); currentEstimatedSize = 0; lastBoundaryIndex_1st = -1; lastBoundaryIndex_2nd = -1;
                    foreach (var overflowNode in overflowNodes)
                    {
                        currentChunk.Add(overflowNode); currentEstimatedSize += overflowNode.Text.Length + yamlOverheadPerNode;
                        if (IsSentenceBoundary_1st(overflowNode.Text)) lastBoundaryIndex_1st = currentChunk.Count - 1;
                        if (IsSentenceBoundary_2nd(overflowNode.Text)) lastBoundaryIndex_2nd = currentChunk.Count - 1;
                    }
                }
                else if (lastBoundaryIndex_1st == currentChunk.Count - 1)
                {
                    SimpleLogger.LogCustom($"[Pass 1] Soft limit with perfect packaging - " + GetChunkLogMessage(currentChunk, chunkIndex), ConsoleColor.White);
                    await WriteChunkAsync(currentChunk, workingDir, chunkIndex++, attributeIdBoundary);
                    currentChunk.Clear(); currentEstimatedSize = 0; lastBoundaryIndex_1st = -1; lastBoundaryIndex_2nd = -1;
                }
                else
                {
                    string LLMrtn = ""; int cutId = -1;
                    if (currentEstimatedSize >= hardLimit)
                    {
                        int cutIndex = -1;
                        if (_useLlmBoundary && _llmService != null)
                        {
                            int? llmResult = await GetCutIdFromLlmAsync(currentChunk);
                            if (llmResult.HasValue)
                            {
                                cutId = llmResult.Value;
                                if (cutId == -1) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine("\n[Pass 1] LLM ans Cut_ID: -1"); Console.ResetColor(); LLMrtn = "-1"; }
                                else
                                {
                                    cutIndex = currentChunk.FindIndex(n => n.Id == cutId);
                                    if (cutIndex == -1) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("\n[Pass 1] LLM ans Cut_ID: failure"); Console.ResetColor(); LLMrtn = "failure"; }
                                    else { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"\n[Pass 1] LLM ans Cut_ID: {cutId}"); Console.ResetColor(); LLMrtn = cutId.ToString(); if (cutIndex == 0) cutIndex = -1; }
                                }
                            }
                            else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("\n[Pass 1] LLM ans Cut_ID: failure"); Console.ResetColor(); LLMrtn = "failure"; }
                        }
                        else { LLMrtn = "(LLM is not used)"; }

                        if (cutIndex > 0 && cutIndex < currentChunk.Count)
                        {
                            var readyChunk = currentChunk.Take(cutIndex).ToList();
                            SimpleLogger.LogCustom($"[Pass 1] LLM backtracking cutting - " + GetChunkLogMessage(readyChunk, chunkIndex), ConsoleColor.White);
                            await WriteChunkAsync(readyChunk, workingDir, chunkIndex++, attributeIdBoundary);
                            var overflowNodes = currentChunk.Skip(cutIndex).ToList();
                            currentChunk.Clear(); currentEstimatedSize = 0; lastBoundaryIndex_1st = -1; lastBoundaryIndex_2nd = -1;
                            foreach (var overflowNode in overflowNodes)
                            {
                                currentChunk.Add(overflowNode); currentEstimatedSize += overflowNode.Text.Length + yamlOverheadPerNode;
                                if (IsSentenceBoundary_1st(overflowNode.Text)) lastBoundaryIndex_1st = currentChunk.Count - 1;
                                if (IsSentenceBoundary_2nd(overflowNode.Text)) lastBoundaryIndex_2nd = currentChunk.Count - 1;
                            }
                        }
                        else
                        {
                            if (lastBoundaryIndex_2nd > 0 && lastBoundaryIndex_2nd < currentChunk.Count - 1)
                            {
                                var readyChunk = currentChunk.Take(lastBoundaryIndex_2nd + 1).ToList();
                                SimpleLogger.LogCustom($"[Pass 1] Hard limit - backtrack with method 2 - " + GetChunkLogMessage(readyChunk, chunkIndex), ConsoleColor.Yellow);
                                await WriteChunkAsync(readyChunk, workingDir, chunkIndex++, attributeIdBoundary);
                                var overflowNodes = currentChunk.Skip(lastBoundaryIndex_2nd + 1).ToList();
                                currentChunk.Clear(); currentEstimatedSize = 0; lastBoundaryIndex_1st = -1; lastBoundaryIndex_2nd = -1;
                                foreach (var overflowNode in overflowNodes)
                                {
                                    currentChunk.Add(overflowNode); currentEstimatedSize += overflowNode.Text.Length + yamlOverheadPerNode;
                                    if (IsSentenceBoundary_1st(overflowNode.Text)) lastBoundaryIndex_1st = currentChunk.Count - 1;
                                    if (IsSentenceBoundary_2nd(overflowNode.Text)) lastBoundaryIndex_2nd = currentChunk.Count - 1;
                                }
                            }
                            else
                            {
                                SimpleLogger.LogCustom($"[Pass 1] Hard limit forced cutting - " + GetChunkLogMessage(currentChunk, chunkIndex) + $" The LLM return: {LLMrtn}", ConsoleColor.Red);
                                await WriteChunkAsync(currentChunk, workingDir, chunkIndex++, attributeIdBoundary);
                                currentChunk.Clear(); currentEstimatedSize = 0; lastBoundaryIndex_1st = -1; lastBoundaryIndex_2nd = -1;
                            }
                        }
                    }
                }
            }
        }
        if (currentChunk.Any())
        {
            SimpleLogger.LogCustom("[Pass 1] Package the last nodes - " + GetChunkLogMessage(currentChunk, chunkIndex), ConsoleColor.White);
            await WriteChunkAsync(currentChunk, workingDir, chunkIndex, attributeIdBoundary);
            return chunkIndex;
        }
        return chunkIndex - 1;
    }

    private async Task<int?> GetCutIdFromLlmAsync(List<NodeEntry> chunk)
    {
        var cleanChunk = chunk.Select(n => new NodeEntry { Id = n.Id, Text = TextCleaner.RemoveTags(n.Text) }).ToList();

        // 這裡也切換為 YAML 格式發送給 LLM 判斷
        string yamlContent = _yamlSerializer.SerializeYamlToUtf8(cleanChunk);

        string systemPrompt = _llmService!.PrepareSystemPrompt(_promptConfig!.BoundaryDetectionPrompt);
        string response = await _llmService.CallLlmAsync(systemPrompt, yamlContent, "\n[Pass 1] Hard limit reached. Asking LLM for boundary...\n");

        try
        {
            string cleanResponse = response.Replace("```", "").Trim();
            if (int.TryParse(cleanResponse, out int cutId)) return cutId;
            var match = Regex.Match(cleanResponse, @"-?\d+");
            if (match.Success && int.TryParse(match.Value, out cutId)) return cutId;
        }
        catch (Exception ex)
        {
            SimpleLogger.LogCustom($"[Pass 1] Warning - LLM boundary detection format failed: {ex.Message.Replace("\r", "").Replace("\n", "")}. Falling back to hard cut.", ConsoleColor.Yellow);
        }
        return null;
    }

    private readonly string[] _SentenceBoundaries_1st = [".", "。", "!", "！", "?", "？", "…", "؟", "।", "።"];
    private readonly string[] _SentenceBoundaries_2nd = [",", "，", ";", "；", ":", "：", "、", "」", "』"];
    private bool IsSentenceBoundary_1st(string text) { /* ... 不變 ... */ if (string.IsNullOrWhiteSpace(text)) return true; string cleanText = TextCleaner.RemoveTags(text).TrimEnd(); if (string.IsNullOrEmpty(cleanText)) return false; foreach (var boundary in _SentenceBoundaries_1st) if (cleanText.EndsWith(boundary)) return true; return false; }
    private bool IsSentenceBoundary_2nd(string text) { /* ... 不變 ... */ if (string.IsNullOrWhiteSpace(text)) return false; string cleanText = TextCleaner.RemoveTags(text).TrimEnd(); if (string.IsNullOrEmpty(cleanText)) return false; foreach (var boundary in _SentenceBoundaries_2nd) if (cleanText.EndsWith(boundary)) return true; return false; }

    // 參數移除了 baseName
    private async Task WriteChunkAsync(List<NodeEntry> chunk, string dir, int index, int attributeBoundary)
    {
        // 寫入 Pass 0 (Node 原型) YAML 檔案
        string nodePath = ProjectFiles.Pass0Chunk(dir, index);
        await File.WriteAllTextAsync(nodePath, _yamlSerializer.SerializeYamlToUtf8(chunk));

        string currentText = string.Join("<SEP>", chunk.Select(n => TextCleaner.RemoveTags(n.Text)));
        string resolvedSourceText = currentText.Replace("<SEP>", " ");

        int currentChunkFirstId = chunk.Count > 0 ? chunk[0].Id : -1;
        bool isAttributeChunk = currentChunkFirstId >= 0 && currentChunkFirstId < attributeBoundary;
        bool isEmptyNode = string.IsNullOrWhiteSpace(currentText.Replace("　", ""));

        if (!_disableSepResolution && !isEmptyNode && !isAttributeChunk && currentText.Contains("<SEP>") && _llmService != null)
        {
            string systemPrompt = _llmService.PrepareSystemPrompt(_promptConfig!.SepResolutionSystemPrompt);
            resolvedSourceText = await _llmService.CallLlmAsync(systemPrompt, currentText, $"\n[Pass 1] Resolving SEP for chunk {index}...\n");
            resolvedSourceText = Regex.Replace(resolvedSourceText, @"[\r\n]+", " ").Trim();
        }

        resolvedSourceText = resolvedSourceText.Replace("<SEP>", " ");
        var pass1Data = new Pass1ChunkData { ResolvedText = resolvedSourceText };

        // 寫入 Pass 1 YAML 檔案
        string pass1Path = ProjectFiles.Pass1Chunk(dir, index);
        await File.WriteAllTextAsync(pass1Path, _yamlSerializer.SerializeYamlToUtf8(pass1Data));
    }

    private string GetFullOpenTag(IElement el) { var attrs = string.Join("", el.Attributes.Select(a => $" {a.Name}=\"{a.Value}\"")); return $"<{el.LocalName}{attrs}>"; }

    // 共用 YAML 寫入方法
    private void SaveYaml<T>(string path, T data)
    {
        File.WriteAllText(path, _yamlSerializer.SerializeYamlToUtf8(data));
    }

    public static class TextCleaner
    {
        private static readonly Regex TagRegex = new Regex(@"<x_\d+>", RegexOptions.Compiled);
        public static string RemoveTags(string input) { if (string.IsNullOrEmpty(input)) return ""; string result = TagRegex.Replace(input, ""); return Regex.Replace(result, @"\s+", " ").Trim(); }
    }

    private string GetChunkLogMessage(List<NodeEntry> nodes, int fileIndex)
    {
        if (nodes == null || nodes.Count == 0) return $"File {fileIndex}: Packaged (Total: 0 nodes).";
        string firstId = nodes[0].Id.ToString(); string lastId = nodes[nodes.Count - 1].Id.ToString(); int totalNodes = nodes.Count;
        return $"File {fileIndex}: Nodes {firstId} - {lastId} packaged (Total: {totalNodes} nodes)";
    }

    private int ExtractAllTargetAttributes(IDocument document, List<NodeEntry> allNodes)
    { /* ... 不變 ... */
        var elements = document.QuerySelectorAll("*");
        var universalAttrs = new[] { "title", "alt", "aria-label", "aria-valuetext", "aria-roledescription", "aria-placeholder", "aria-description", "data-label", "data-tooltip", "data-text", "data-msg" };
        var allowedMetaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "description", "twitter:title", "twitter:description", "twitter:image:alt" };
        var allowedMetaProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "og:title", "og:description", "og:site_name" };

        foreach (var el in elements)
        {
            foreach (var attr in universalAttrs) TryExtractAttribute(el, attr, allNodes);
            string tagName = el.LocalName.ToLower();
            switch (tagName)
            {
                case "title": string titleText = el.TextContent; if (!string.IsNullOrWhiteSpace(titleText)) { int currentId = _nodeCounter++; allNodes.Add(new NodeEntry { Id = currentId, Text = titleText }); el.TextContent = $"[[htmtxt_ID_{currentId}]]"; } break;
                case "input": string type = (el.GetAttribute("type") ?? "").ToLower(); if (type == "button" || type == "submit" || type == "reset") TryExtractAttribute(el, "value", allNodes); TryExtractAttribute(el, "placeholder", allNodes); break;
                case "textarea": TryExtractAttribute(el, "placeholder", allNodes); break;
                case "optgroup": case "track": case "option": TryExtractAttribute(el, "label", allNodes); break;
                case "th": TryExtractAttribute(el, "abbr", allNodes); break;
                case "a": case "area": TryExtractAttribute(el, "download", allNodes); break;
                case "meta": string name = el.GetAttribute("name") ?? ""; string property = el.GetAttribute("property") ?? ""; if (allowedMetaNames.Contains(name) || allowedMetaProps.Contains(property)) TryExtractAttribute(el, "content", allNodes); break;
            }
        }
        return _nodeCounter;
    }

    private void TryExtractAttribute(IElement el, string attrName, List<NodeEntry> allNodes)
    {
        if (el.HasAttribute(attrName)) { string val = el.GetAttribute(attrName); if (!string.IsNullOrWhiteSpace(val)) { int currentId = _nodeCounter++; allNodes.Add(new NodeEntry { Id = currentId, Text = val }); el.SetAttribute(attrName, $"[[htmtxt_ID_{currentId}]]"); } }
    }
}