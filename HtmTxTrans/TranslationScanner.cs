using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace HtmTxTrans;

public class TranslationScanner
{
    private readonly LlmService _llm;
    private readonly AppConfig _config;
    private readonly LlmPromptConfig _promptConfig;

    private readonly ISerializer _yamlSerializer;
    private readonly IDeserializer _yamlDeserializer;

    public TranslationScanner(AppConfig config, LlmPromptConfig promptConfig, LlmService llm)
    {
        _config = config;
        _promptConfig = promptConfig;
        _llm = llm;

        _yamlSerializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    // 用來承載 Pass 3 結果的小型類別
    public class Pass3Result
    {
        public string OriginalText { get; set; } = "";
        public string TranslatedText { get; set; } = "";
    }

    // ==========================================
    // Pass 3: 負責上下文翻譯
    // ==========================================
    public async Task TranslateToTextAsync(string workingDir, string baseName, string inputFileName, int targetChunk, bool continuous)
    {
        var files = Utitilty.GetSortedPass1Files(workingDir, baseName);
        if (files == null || !files.Any()) return;

        int maxIndex = files.Count - 1;

        if (targetChunk < 0 || targetChunk > maxIndex)
        {
            SimpleLogger.LogCustom($"[Pass 3] Error: Target chunk {targetChunk} is out of bounds (0 - {maxIndex}).", ConsoleColor.Red);
            return;
        }

        int startIdx = targetChunk;
        int endIdx = continuous ? maxIndex : targetChunk;

        var metadata = ProjectMetadata.LoadMetadata(ProjectFiles.Metadata(workingDir));
        int attributeBoundary = metadata?.AttributeIdBoundary ?? -1;
        var fullGlossary = LoadGlossary(workingDir);

        for (int i = startIdx; i <= endIdx; i++)
        {
            // 若此處拋出 Exception (如 Socket API Error)，將直接中止並結束程式
            await ProcessTranslateChunkAsync(workingDir, i, files.Count, fullGlossary, attributeBoundary);
        }

        SimpleLogger.LogCustom(Utitilty.PadCenterCustom($" [Pass 3] Processed chunks {startIdx} to {endIdx} ", 60, '='));
    }

    // ==========================================
    // Pass 4: 負責將純文字與原本的 Node YAML 對齊
    // ==========================================
    public async Task NodeAlignTranslationAsync(string workingDir, string baseName, string inputFileName, int targetChunk, bool continuous)
    {
        var files = Utitilty.GetSortedPass1Files(workingDir, baseName);
        if (files == null || !files.Any()) return;

        int maxIndex = files.Count - 1;

        if (targetChunk < 0 || targetChunk > maxIndex)
        {
            SimpleLogger.LogCustom($"[Pass 4] Error: Target chunk {targetChunk} is out of bounds (0 - {maxIndex}).", ConsoleColor.Red);
            return;
        }

        int startIdx = targetChunk;
        int endIdx = continuous ? maxIndex : targetChunk;

        var tagRegex = new Regex(@"<x_\d+>", RegexOptions.Compiled);
        List<int> failedChunks = new List<int>();

        for (int i = startIdx; i <= endIdx; i++)
        {
            bool isSuccess = await ProcessNodeAlignChunkAsync(workingDir, i, files.Count, tagRegex, 0);

            if (!isSuccess)
            {
                failedChunks.Add(i);
                if (continuous || targetChunk == 0)
                {
                    string failurePath = ProjectFiles.Pass4Failure(workingDir);
                    await SaveFailureFileAsync(failurePath, 0, failedChunks);
                }
            }
        }

        Console.WriteLine("");
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom($" [Pass 4] Node Aligned chunks {startIdx} to {endIdx} ", 60, '='));
    }

    // ==========================================
    // Pass 5: 負責將剝離的 Tag 重新映射回 Node 裡
    // ==========================================
    public async Task TagAlignTranslationAsync(string workingDir, string baseName, string inputFileName, int targetChunk, bool continuous)
    {
        var files = Utitilty.GetSortedPass1Files(workingDir, baseName);
        if (files == null || !files.Any()) return;

        int maxIndex = files.Count - 1;

        if (targetChunk < 0 || targetChunk > maxIndex)
        {
            SimpleLogger.LogCustom($"[Pass 5] Error: Target chunk {targetChunk} is out of bounds (0 - {maxIndex}).", ConsoleColor.Red);
            return;
        }

        int startIdx = targetChunk;
        int endIdx = continuous ? maxIndex : targetChunk;

        var tagRegex = new Regex(@"<x_\d+>", RegexOptions.Compiled);
        List<int> failedChunks = new List<int>();

        var holdDict = LoadHoldData(ProjectFiles.Hold(workingDir));

        for (int i = startIdx; i <= endIdx; i++)
        {
            bool isSuccess = await ProcessTagAlignChunkAsync(workingDir, i, files.Count, tagRegex, 0, holdDict);

            if (!isSuccess)
            {
                failedChunks.Add(i);
                if (continuous || targetChunk == 0)
                {
                    string failurePath = ProjectFiles.Pass5Failure(workingDir);
                    await SaveFailureFileAsync(failurePath, 0, failedChunks);
                }
            }
        }

        Console.WriteLine("");
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom($" [Pass 5] Tag Aligned chunks {startIdx} to {endIdx} ", 60, '='));
    }

    // ==========================================
    // Process Chunk Logic
    // ==========================================

    private async Task<bool> ProcessTranslateChunkAsync(string workingDir, int i, int totalFilesCount, Dictionary<string, string> fullGlossary, int attributeBoundary)
    {
        string phaseLogName = "Pass 3";
        string nodeFilePath = ProjectFiles.Pass0Chunk(workingDir, i);
        string pass1FilePath = ProjectFiles.Pass1Chunk(workingDir, i);
        string outputPath = ProjectFiles.Pass3Chunk(workingDir, i);

        string nodeYaml = await File.ReadAllTextAsync(nodeFilePath);
        var nodes = _yamlDeserializer.Deserialize<List<NodeEntry>>(nodeYaml) ?? new List<NodeEntry>();

        string pass1Yaml = await File.ReadAllTextAsync(pass1FilePath);
        var pass1Data = _yamlDeserializer.Deserialize<Pass1ChunkData>(pass1Yaml);

        if (!nodes.Any() || pass1Data == null) return true;

        int currentChunkFirstId = nodes[0].Id;
        bool isAttributeChunk = currentChunkFirstId >= 0 && currentChunkFirstId < attributeBoundary;

        string pastText = "";
        string nextText = "";
        string currentTextWithSep = string.Join("<SEP>", nodes.Select(n => HtmlProcessor.TextCleaner.RemoveTags(n.Text)));
        string resolvedSourceText = pass1Data.ResolvedText;

        int currentLength = resolvedSourceText.Length;
        double usableSpace = (_config.SlidingWindowSize * 0.5) - currentLength;

        int preChar = (int)(usableSpace * 0.66);
        int nextChar = (int)(usableSpace * 0.33);

        preChar = Math.Max(40, Math.Min(300, preChar));
        nextChar = Math.Max(20, Math.Min(150, nextChar));

        if (!isAttributeChunk)
        {
            if (i > 0)
            {
                int prevChunkFirstId = GetFirstNodeId(workingDir, i - 1);
                if (prevChunkFirstId >= attributeBoundary)
                {
                    pastText = ContextHelper.GetSmartPastContext(ExtractPureText(workingDir, i - 1), preChar);
                }
            }

            if (i < totalFilesCount - 1)
            {
                nextText = ContextHelper.GetSmartNextContext(ExtractPureText(workingDir, i + 1), nextChar);
            }
        }
        else
        {
            Console.WriteLine("");
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} is an attribute chunk. Context is intentionally isolated.", ConsoleColor.DarkGray);
        }

        bool isEmptyNode = string.IsNullOrWhiteSpace(resolvedSourceText.Replace("　", ""));

        string translatedTxt = resolvedSourceText;
        bool isSuccess = false;

        if (isEmptyNode)
        {
            isSuccess = true;
            Console.WriteLine("");
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : Empty text node detected. Skipped LLM translation.", ConsoleColor.DarkGray);
        }
        else
        {
            var filteredGlossary = fullGlossary
                .Where(kvp => resolvedSourceText.Contains(kvp.Key) || currentTextWithSep.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            string yamlRef = "{}";
            if (filteredGlossary.Any())
            {
                // 序列化為 YAML 並加上縮排，以利鑲嵌至純 YAML Prompt
                yamlRef = _yamlSerializer.SerializeYamlToUtf8(filteredGlossary).TrimEnd().Replace("\n", "\n  ");
            }

            string userPrompt = _promptConfig.TranslationUserPrompt
                .Replace("<<preContext>>", pastText)
                .Replace("<<currentContext>>", resolvedSourceText)
                .Replace("<<nextContext>>", nextText)
                .Replace("<<Glossary>>", yamlRef);

            string systemPrompt = _llm.PrepareSystemPrompt(_promptConfig.TranslationSystemPrompt);

            // API 錯誤直接拋出，中止進程
            translatedTxt = await _llm.CallLlmAsync(systemPrompt, userPrompt, $"\n[{phaseLogName}] Start translation: chunk {i} of {totalFilesCount - 1}\n");
            isSuccess = true;
            Console.WriteLine("");
        }

        // 把換行字元清除，確保維持單行格式
        translatedTxt = Regex.Replace(translatedTxt, @"[\r\n]+", " ");

        var pass3Result = new Pass3Result
        {
            OriginalText = resolvedSourceText,
            TranslatedText = translatedTxt
        };

        await File.WriteAllTextAsync(outputPath, _yamlSerializer.SerializeYamlToUtf8(pass3Result));

        if (isSuccess && !isEmptyNode)
        {
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : Translate Done.", ConsoleColor.White);
        }

        Console.WriteLine("\n" + new string('-', 20));

        return isSuccess;
    }

    private async Task<bool> ProcessNodeAlignChunkAsync(string workingDir, int i, int totalFilesCount, Regex tagRegex, int retryCount)
    {
        string nodeFilePath = ProjectFiles.Pass0Chunk(workingDir, i);
        string pass3FilePath = ProjectFiles.Pass3Chunk(workingDir, i);
        string pass4FilePath = ProjectFiles.Pass4Chunk(workingDir, i);

        bool IsRetry = retryCount > 0;
        string phaseLogName = IsRetry ? $"Pass 4 Retry {retryCount}" : "Pass 4";

        bool isFileReady = await WaitForFileOrFallbackAsync(pass3FilePath, phaseLogName);
        string currentYamlNodeStr = await File.ReadAllTextAsync(nodeFilePath);

        if (!isFileReady)
        {
            await File.WriteAllTextAsync(pass4FilePath, currentYamlNodeStr);
            SimpleLogger.LogCustom($"[{phaseLogName}] Bypassed alignment due to forced fallback.", ConsoleColor.Red);
            return false;
        }

        string pass3Yaml = await File.ReadAllTextAsync(pass3FilePath);
        string originalText = "";
        string translatedText = "";

        try
        {
            var p3Obj = _yamlDeserializer.Deserialize<Pass3Result>(pass3Yaml);
            originalText = p3Obj?.OriginalText ?? "";
            translatedText = p3Obj?.TranslatedText ?? "";
        }
        catch
        {
            SimpleLogger.LogCustom($"[{phaseLogName}] Error - Failed to parse {pass3FilePath}. Using empty strings.", ConsoleColor.Red);
        }

        bool isEmptyNode = string.IsNullOrWhiteSpace(originalText.Replace("　", ""));
        bool isSameText = originalText.Trim() == translatedText.Trim();

        string finalYamlToWrite = currentYamlNodeStr;
        bool isSuccess = false;
        string errorMsg = "";
        bool fallbackToPass0 = false;
        ConsoleColor logColor = ConsoleColor.White;

        if (isEmptyNode || isSameText)
        {
            finalYamlToWrite = currentYamlNodeStr;
            isSuccess = true;
            Console.WriteLine("");
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : Empty or Identical text. Bypassed Node Alignment.", ConsoleColor.DarkGray);
        }
        else
        {
            List<NodeEntry> inputNodes = new List<NodeEntry>();
            List<NodeEntry> outputNodes4 = new List<NodeEntry>();
            HashSet<int> validIds = new HashSet<int>();

            isSuccess = true;

            try
            {
                inputNodes = _yamlDeserializer.Deserialize<List<NodeEntry>>(currentYamlNodeStr) ?? new List<NodeEntry>();
                validIds = inputNodes.Select(n => n.Id).ToHashSet();

                // 把 Node 裡的標籤清空後再轉換成 YAML 字串，避免直接用 Regex 替換 YAML 字串導致格式損毀
                var taglessNodes = inputNodes.Select(n => new NodeEntry { Id = n.Id, Text = tagRegex.Replace(n.Text, " ") }).ToList();
                string currentYamlTagless = _yamlSerializer.SerializeYamlToUtf8(taglessNodes).TrimEnd().Replace("\n", "\n  ");

                string userPrompt4 = _promptConfig.AlignmentUserPrompt
                    .Replace("<<CurrentJson>>", currentYamlTagless) // 注意：模型中變數名若為 <<CurrentJson>> 仍可匹配
                    .Replace("<<OriginalText>>", originalText)
                    .Replace("<<TranslatedText>>", translatedText);

                string systemPrompt4 = _llm.PrepareSystemPrompt(_promptConfig.AlignmentSystemPrompt);

                // API錯誤會在此中斷
                string rawResponse4 = await _llm.CallLlmAsync(systemPrompt4, userPrompt4, $"\nStart aligning chunk [{phaseLogName}]: {i} of {totalFilesCount - 1}\n");

                string cleanedYaml4 = CleanLlmYaml(rawResponse4);

                outputNodes4 = _yamlDeserializer.Deserialize<List<NodeEntry>>(cleanedYaml4);
                if (outputNodes4 == null) throw new Exception("Invalid YAML format.");

                var outputIds4 = outputNodes4.Select(n => n.Id).ToHashSet();

                var extraIds = outputIds4.Except(validIds).ToList();
                if (extraIds.Any()) throw new Exception("Extra nodes detected (Out of bounds ID).");

                var missingIds = validIds.Except(outputIds4).ToList();
                if (missingIds.Any())
                {
                    foreach (var id in missingIds)
                    {
                        var originalNode = inputNodes.First(n => n.Id == id);
                        string textWithoutTags = tagRegex.Replace(originalNode.Text, " ");
                        outputNodes4.Add(new NodeEntry { Id = id, Text = textWithoutTags });
                    }
                    outputNodes4 = outputNodes4.OrderBy(n => n.Id).ToList();
                    isSuccess = false;
                    errorMsg = "Missing ID (Patched from Pass 0)";
                    logColor = ConsoleColor.Yellow;
                }
            }
            catch (Exception ex)
            {
                // YAML 解析錯誤或資料丟失等問題
                isSuccess = false;
                errorMsg = ex.Message;
                fallbackToPass0 = true;
                logColor = ConsoleColor.Red;
            }

            if (fallbackToPass0)
            {
                finalYamlToWrite = currentYamlNodeStr;
            }
            else
            {
                finalYamlToWrite = _yamlSerializer.SerializeYamlToUtf8(outputNodes4);
            }
        }

        if (isSuccess || (!isSuccess && !IsRetry))
        {
            await File.WriteAllTextAsync(pass4FilePath, finalYamlToWrite);
        }

        if (isSuccess && !isEmptyNode && !isSameText)
        {
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : Node Alignment Done.", ConsoleColor.White);
        }
        else if (!isSuccess)
        {
            string fallbackStatus = fallbackToPass0 ? "Fallback to Pass 0" : "Patched/Auto-fixed";
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : {fallbackStatus}. {errorMsg}", logColor);
        }

        Console.WriteLine("\n" + new string('-', 20));
        return isSuccess;
    }

    private async Task<bool> ProcessTagAlignChunkAsync(string workingDir, int i, int totalFilesCount, Regex tagRegex, int retryCount, Dictionary<string, string> holdDict)
    {
        string nodeFilePath = ProjectFiles.Pass0Chunk(workingDir, i);
        string pass4FilePath = ProjectFiles.Pass4Chunk(workingDir, i);
        string finalFilePath = ProjectFiles.FinalChunk(workingDir, i);

        bool IsRetry = retryCount > 0;
        string phaseLogName = IsRetry ? $"Pass 5 Retry {retryCount}" : "Pass 5";

        bool isFileReady = await WaitForFileOrFallbackAsync(pass4FilePath, phaseLogName);
        string currentYamlNodeStr = await File.ReadAllTextAsync(nodeFilePath);

        if (!isFileReady)
        {
            await File.WriteAllTextAsync(finalFilePath, currentYamlNodeStr);
            SimpleLogger.LogCustom($"[{phaseLogName}] Bypassed alignment due to forced fallback.", ConsoleColor.Red);
            return false;
        }

        string pass4Yaml = await File.ReadAllTextAsync(pass4FilePath);

        List<NodeEntry> inputNodes = _yamlDeserializer.Deserialize<List<NodeEntry>>(currentYamlNodeStr) ?? new List<NodeEntry>();
        List<NodeEntry> pass4Nodes = _yamlDeserializer.Deserialize<List<NodeEntry>>(pass4Yaml) ?? new List<NodeEntry>();

        bool hasAnyTags = inputNodes.Any(n => tagRegex.IsMatch(n.Text));

        string finalYamlToWrite = pass4Yaml;
        bool isSuccess = true;
        string errorMsg = "";
        bool fallbackToPass4 = false;
        ConsoleColor logColor = ConsoleColor.White;

        if (!hasAnyTags)
        {
            finalYamlToWrite = pass4Yaml;
            isSuccess = true;
            Console.WriteLine("");
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : No tags detected. Bypassed Tag Alignment.", ConsoleColor.DarkGray);
        }
        else
        {
            List<NodeEntry> outputNodes5 = new List<NodeEntry>();
            HashSet<int> validIds = inputNodes.Select(n => n.Id).ToHashSet();
            bool hasClosingTagHallucination = false;

            try
            {
                // 替換 YAML 到變數裡，加縮排防禦
                string originalYamlStr = _yamlSerializer.SerializeYamlToUtf8(inputNodes).TrimEnd().Replace("\n", "\n  ");
                string nodeAlignedYamlStr = _yamlSerializer.SerializeYamlToUtf8(pass4Nodes).TrimEnd().Replace("\n", "\n  ");

                string userPrompt5 = _promptConfig.TagAlignmentUserPrompt
                    .Replace("<<CurrentJson>>", originalYamlStr)
                    .Replace("<<NodeAlignedJson>>", nodeAlignedYamlStr);

                string systemPrompt5 = _llm.PrepareSystemPrompt(_promptConfig.TagAlignmentSystemPrompt);

                // API錯誤會在此中斷
                string rawResponse5 = await _llm.CallLlmAsync(systemPrompt5, userPrompt5, $"\nStart tag alignment chunk [{phaseLogName}]: {i} of {totalFilesCount - 1}\n");

                string cleanedYaml5 = CleanLlmYaml(rawResponse5);

                hasClosingTagHallucination = Regex.IsMatch(cleanedYaml5, @"<\/\s*x_\d+\s*>");
                if (hasClosingTagHallucination)
                {
                    cleanedYaml5 = Regex.Replace(cleanedYaml5, @"<\/\s*x_(\d+)\s*>", "<x_$1>");
                }

                outputNodes5 = _yamlDeserializer.Deserialize<List<NodeEntry>>(cleanedYaml5);
                if (outputNodes5 == null) throw new Exception("Invalid YAML format.");

                var outputIds5 = outputNodes5.Select(n => n.Id).ToHashSet();
                if (outputIds5.Except(validIds).Any() || validIds.Except(outputIds5).Any())
                {
                    throw new Exception("ID mismatch (Lost or extra nodes).");
                }

                bool has5OtherError = false;
                foreach (var inNode in inputNodes)
                {
                    var outNode = outputNodes5.FirstOrDefault(n => n.Id == inNode.Id);
                    if (outNode == null) continue;

                    var inTagsRaw = tagRegex.Matches(inNode.Text).Select(m => m.Value).ToList();
                    var outTagsRaw = tagRegex.Matches(outNode.Text).Select(m => m.Value).ToList();

                    var inTagsSorted = inTagsRaw.OrderBy(x => x).ToList();
                    var outTagsSorted = outTagsRaw.OrderBy(x => x).ToList();

                    if (!inTagsSorted.SequenceEqual(outTagsSorted))
                    {
                        var missingTags = inTagsSorted.Except(outTagsSorted).ToList();
                        var extraTags = outTagsSorted.Except(inTagsSorted).ToList();

                        if (extraTags.Any())
                        {
                            isSuccess = false;
                            errorMsg = "Other error (Extra tags out of bounds)";
                            logColor = ConsoleColor.Yellow;
                            has5OtherError = true;
                            break;
                        }
                        else if (missingTags.Any())
                        {
                            isSuccess = false;
                            errorMsg = "Missing tags (Specific node fell back to Pass 4)";
                            logColor = ConsoleColor.Yellow;

                            var node4 = pass4Nodes.FirstOrDefault(n => n.Id == inNode.Id);
                            if (node4 != null) outNode.Text = node4.Text;
                        }
                    }
                    else if (!inTagsRaw.SequenceEqual(outTagsRaw))
                    {
                        // [新增] 先進行結構合法性驗證
                        if (ValidateTagStructure(inTagsRaw, outTagsRaw, holdDict))
                        {
                            // 結構合法，接受 LLM 的調換順序
                            // (不改變 outNode.Text，也不標記為失敗，默默讓它通過)
                        }
                        else
                        {
                            isSuccess = false;
                            errorMsg = "Tag sequence error (Auto-rearranged)";
                            logColor = ConsoleColor.Yellow;

                            var textParts = tagRegex.Split(outNode.Text);
                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            for (int t = 0; t < textParts.Length; t++)
                            {
                                sb.Append(textParts[t]);
                                if (t < inTagsRaw.Count)
                                {
                                    sb.Append(inTagsRaw[t]);
                                }
                            }
                            outNode.Text = sb.ToString();
                        }
                    }
                }

                if (has5OtherError)
                {
                    throw new Exception(errorMsg);
                }

                if (isSuccess && hasClosingTagHallucination)
                {
                    isSuccess = false;
                    errorMsg = "Tag hallucination (Auto-fixed but needs retry)";
                    logColor = ConsoleColor.Yellow;
                }

            }
            catch (Exception ex)
            {
                isSuccess = false;
                errorMsg = ex.Message;
                fallbackToPass4 = true;
                logColor = ConsoleColor.Yellow;
            }

            if (fallbackToPass4)
            {
                finalYamlToWrite = pass4Yaml;
            }
            else
            {
                finalYamlToWrite = _yamlSerializer.SerializeYamlToUtf8(outputNodes5);
            }
        }

        if (isSuccess || (!isSuccess && !IsRetry))
        {
            await File.WriteAllTextAsync(finalFilePath, finalYamlToWrite);
        }

        if (isSuccess && hasAnyTags)
        {
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : Tag Alignment Done.", ConsoleColor.White);
        }
        else if (!isSuccess)
        {
            string fallbackStatus = fallbackToPass4 ? "Fallback to Pass 4" : "Patched/Auto-fixed";
            SimpleLogger.LogCustom($"[{phaseLogName}] Chunk {i} of {totalFilesCount - 1} : {fallbackStatus}. {errorMsg}", logColor);
        }

        Console.WriteLine("\n" + new string('-', 20));
        return isSuccess;
    }

    private async Task RunPass4RetryLoopAsync(string workingDir, int totalFilesCount, Regex tagRegex, string inputFileName, string failurePath)
    {
        while (true)
        {
            var yaml = await File.ReadAllTextAsync(failurePath);
            var failureRecord = _yamlDeserializer.Deserialize<Dictionary<string, int>>(yaml);
            if (failureRecord == null || !failureRecord.ContainsKey("Retry")) break;

            int currentRetry = failureRecord["Retry"];

            if (currentRetry >= _config.Pass4RetryMax)
            {
                var remainingCount = failureRecord.Count(kv => kv.Key.StartsWith("err_"));
                if (remainingCount > 0)
                    SimpleLogger.LogCustom($"[Pass 4 Retry] Reached maximum retry limit ({_config.Pass4RetryMax}). Remaining failures/warnings: {remainingCount}");
                break;
            }

            var chunksToRetry = failureRecord.Where(kv => kv.Key.StartsWith("err_")).Select(kv => kv.Value).ToList();
            if (!chunksToRetry.Any())
            {
                SimpleLogger.LogCustom($"[Pass 4 Retry] No failed chunks to retry. Retries completed successfully.");
                break;
            }

            Console.WriteLine($"\n[Pass 4 Retry] Retry {currentRetry + 1}/{_config.Pass4RetryMax} : Retrying {chunksToRetry.Count} failed chunks...");

            List<int> currentFailedChunks = new List<int>(chunksToRetry);

            foreach (int i in chunksToRetry)
            {
                bool isSuccess = await ProcessNodeAlignChunkAsync(workingDir, i, totalFilesCount, tagRegex, currentRetry + 1);

                if (isSuccess)
                {
                    currentFailedChunks.Remove(i);
                    await SaveFailureFileAsync(failurePath, currentRetry, currentFailedChunks);
                }
            }

            await SaveFailureFileAsync(failurePath, currentRetry + 1, currentFailedChunks);

            if (!currentFailedChunks.Any())
            {
                SimpleLogger.LogCustom($"[Pass 4 Retry] All chunks successfully aligned after {currentRetry + 1} retries!");
                break;
            }
        }
    }

    private async Task RunPass5RetryLoopAsync(string workingDir, int totalFilesCount, Regex tagRegex, string inputFileName, string failurePath)
    {
        while (true)
        {
            var yaml = await File.ReadAllTextAsync(failurePath);
            var failureRecord = _yamlDeserializer.Deserialize<Dictionary<string, int>>(yaml);
            if (failureRecord == null || !failureRecord.ContainsKey("Retry")) break;

            int currentRetry = failureRecord["Retry"];

            if (currentRetry >= _config.Pass5RetryMax)
            {
                var remainingCount = failureRecord.Count(kv => kv.Key.StartsWith("err_"));
                if (remainingCount > 0)
                    SimpleLogger.LogCustom($"[Pass 5 Retry] Reached maximum retry limit ({_config.Pass5RetryMax}). Remaining failures/warnings: {remainingCount}");
                break;
            }

            var chunksToRetry = failureRecord.Where(kv => kv.Key.StartsWith("err_")).Select(kv => kv.Value).ToList();
            if (!chunksToRetry.Any())
            {
                SimpleLogger.LogCustom($"[Pass 5 Retry] No failed chunks to retry. Retries completed successfully.");
                break;
            }

            Console.WriteLine($"\n[Pass 5 Retry] Retry {currentRetry + 1}/{_config.Pass5RetryMax} : Retrying {chunksToRetry.Count} failed chunks...");

            List<int> currentFailedChunks = new List<int>(chunksToRetry);

            // [新增] 載入 Hold 字典
            var holdDict = LoadHoldData(ProjectFiles.Hold(workingDir));

            foreach (int i in chunksToRetry)
            {

                bool isSuccess = await ProcessTagAlignChunkAsync(workingDir, i, totalFilesCount, tagRegex, currentRetry + 1, holdDict);

                if (isSuccess)
                {
                    currentFailedChunks.Remove(i);
                    await SaveFailureFileAsync(failurePath, currentRetry, currentFailedChunks);
                }
            }

            await SaveFailureFileAsync(failurePath, currentRetry + 1, currentFailedChunks);

            if (!currentFailedChunks.Any())
            {
                SimpleLogger.LogCustom($"[Pass 5 Retry] All chunks successfully aligned after {currentRetry + 1} retries!");
                break;
            }
        }
    }

    private async Task SaveFailureFileAsync(string path, int retryCount, List<int> failedChunks)
    {
        var record = new Dictionary<string, int> { { "Retry", retryCount } };
        for (int k = 0; k < failedChunks.Count; k++)
        {
            record[$"err_{k}"] = failedChunks[k];
        }
        await File.WriteAllTextAsync(path, _yamlSerializer.SerializeYamlToUtf8(record));
    }

    private Dictionary<string, string> LoadGlossary(string workingDir)
    {
        string glossaryPath = ProjectFiles.Pass2Glossary(workingDir);
        if (!File.Exists(glossaryPath)) return new Dictionary<string, string>();

        try
        {
            var rawGlossary = _yamlDeserializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(glossaryPath));
            if (rawGlossary == null) return new Dictionary<string, string>();

            return rawGlossary.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                              .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        catch { return new Dictionary<string, string>(); }
    }

    private string ExtractPureText(string workingDir, int index)
    {
        try
        {
            string pass1FilePath = ProjectFiles.Pass1Chunk(workingDir, index);
            var data = _yamlDeserializer.Deserialize<Pass1ChunkData>(File.ReadAllText(pass1FilePath));
            return data?.ResolvedText ?? "";
        }
        catch { return ""; }
    }

    private string CleanLlmYaml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var match = Regex.Match(input, @"```(?:yaml)?\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return input.Trim();
    }

    public async Task RetryNodeAlignAsync(string workingDir, string baseName, string inputFileName)
    {
        if (_config.Pass4RetryMax == 0)
        {
            SimpleLogger.LogCustom("[Pass 4 Retry] Pass4RetryMax is set to 0. Retry is disabled.");
            return;
        }

        string failurePath = ProjectFiles.Pass4Failure(workingDir);
        if (!File.Exists(failurePath))
        {
            SimpleLogger.LogCustom("[Pass 4 Retry] Pass4.failure.yaml not found. Nothing to retry.");
            return;
        }

        var files = Utitilty.GetSortedPass1Files(workingDir, baseName);
        if (!files.Any()) return;

        var tagRegex = new Regex(@"<x_\d+>", RegexOptions.Compiled);

        Console.WriteLine("[Pass 4 Retry] Starting retry process...");
        ResetRetryCountInFile(failurePath);
        await RunPass4RetryLoopAsync(workingDir, files.Count, tagRegex, inputFileName, failurePath);
    }

    public async Task RetryTagAlignAsync(string workingDir, string baseName, string inputFileName)
    {
        if (_config.Pass5RetryMax == 0)
        {
            SimpleLogger.LogCustom("[Pass 5 Retry] Pass5RetryMax is set to 0. Retry is disabled.");
            return;
        }

        string failurePath = ProjectFiles.Pass5Failure(workingDir);
        if (!File.Exists(failurePath))
        {
            SimpleLogger.LogCustom("[Pass 5 Retry] Pass5.failure.yaml not found. Nothing to retry.");
            return;
        }

        var files = Utitilty.GetSortedPass1Files(workingDir, baseName);
        if (!files.Any()) return;

        var tagRegex = new Regex(@"<x_\d+>", RegexOptions.Compiled);

        Console.WriteLine("[Pass 5 Retry] Starting retry process...");
        ResetRetryCountInFile(failurePath);
        await RunPass5RetryLoopAsync(workingDir, files.Count, tagRegex, inputFileName, failurePath);
    }

    public void ResetRetryCountInFile(string failureFile)
    {
        if (!File.Exists(failureFile)) return;

        try
        {
            string yamlContent = File.ReadAllText(failureFile);
            var dict = _yamlDeserializer.Deserialize<Dictionary<string, int>>(yamlContent);

            if (dict != null && dict.ContainsKey("Retry"))
            {
                dict["Retry"] = 0;
                string updatedYaml = _yamlSerializer.SerializeYamlToUtf8(dict);
                File.WriteAllText(failureFile, updatedYaml);
            }
        }
        catch { }
    }

    private int GetFirstNodeId(string workingDir, int index)
    {
        try
        {
            string nodeFilePath = ProjectFiles.Pass0Chunk(workingDir, index);
            var nodes = _yamlDeserializer.Deserialize<List<NodeEntry>>(File.ReadAllText(nodeFilePath));
            return nodes != null && nodes.Count > 0 ? nodes[0].Id : -1;
        }
        catch { return -1; }
    }

    private async Task<bool> WaitForFileOrFallbackAsync(string filePath, string phaseLogName)
    {
        if (File.Exists(filePath))
        {
            return true;
        }

        SimpleLogger.LogCustom($"[{phaseLogName}] Waiting for file: {Path.GetFileName(filePath)} ... (Press [F] to force Fallback, Ctrl+C to exit)", ConsoleColor.Cyan);

        while (!File.Exists(filePath))
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.F)
                {
                    SimpleLogger.LogCustom($"[{phaseLogName}] User pressed [F]. Forcing fallback for {Path.GetFileName(filePath)}", ConsoleColor.Red);
                    return false;
                }
            }
            await Task.Delay(500);
        }

        SimpleLogger.LogCustom($"[{phaseLogName}] File detected: {Path.GetFileName(filePath)}. Resuming...", ConsoleColor.Green);
        return true;
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
        catch { return new Dictionary<string, string>(); }
    }

    private bool ValidateTagStructure(List<string> inTagsRaw, List<string> outTagsRaw, Dictionary<string, string> holdDict)
    {
        if (holdDict == null || holdDict.Count == 0) return false;

        // 內部驗證函數：將 <x_n> 陣列展開成 HTML 後進行 Stack 驗證
        bool IsValidSequence(List<string> sequence)
        {
            // 1. 將標籤還原成真實 HTML 組合片段
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (var tag in sequence)
            {
                if (holdDict.TryGetValue(tag, out var content))
                {
                    sb.Append(content);
                }
            }
            string fullHtml = sb.ToString();

            // 定義不需要閉合的空元素
            var voidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "br", "img", "hr", "meta", "input", "link", "area", "base", "col", "param", "source", "track", "wbr"
            };

            // 抓出字串中所有 HTML 標籤
            var matches = Regex.Matches(fullHtml, @"<\s*(/?)\s*([a-zA-Z0-9\-]+)[^>]*>");
            var stack = new Stack<string>();

            foreach (Match m in matches)
            {
                // 如果是自閉合標籤 (如 <img />)，直接跳過
                if (m.Value.EndsWith("/>")) continue;

                bool isClosing = m.Groups[1].Value == "/";
                string tagName = m.Groups[2].Value.ToLower();

                // [關鍵防禦] 如果是空元素 (如 br, img)，無論開關都完全不參與 Stack 計算
                if (voidElements.Contains(tagName)) continue;

                if (!isClosing)
                {
                    stack.Push(tagName);
                }
                else
                {
                    // 遇到閉合標籤，但 Stack 已空 (代表提早閉合)
                    if (stack.Count == 0) return false;

                    string top = stack.Pop();
                    // 遇到閉合標籤，但不符合最後一個開啟的標籤 (代表交錯，如 <a><b></a></b>)
                    if (top != tagName) return false;
                }
            }
            // 若最後 Stack 完美清空，代表結構完全合法
            return stack.Count == 0;
        }

        // [安全網] 確保我們的驗證邏輯能順利解析原始 HTML
        // 如果連原始結構驗證都失敗 (極端罕見的未知標籤)，我們就保守地退回 False (拒絕 LLM 調換)
        if (!IsValidSequence(inTagsRaw)) return false;

        // 正式驗證 LLM 輸出的順序
        return IsValidSequence(outTagsRaw);
    }
}