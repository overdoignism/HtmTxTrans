using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using System.IO;
// using FuzzySharp; // 【已註解】移除 FuzzySharp 依賴，降低分發龐雜性
using YamlDotNet.Serialization;
using System.Threading.Tasks;

namespace HtmTxTrans;

public class ProperNounScanner
{
    private readonly LlmService _llm;
    private readonly LlmPromptConfig _promptConfig;
    private readonly Dictionary<string, int> _frequencyMap = new(StringComparer.OrdinalIgnoreCase);

    private readonly ISerializer _yamlSerializer;
    private readonly IDeserializer _yamlDeserializer;

    public ProperNounScanner(LlmPromptConfig promptConfig, LlmService llm)
    {
        _promptConfig = promptConfig;
        _llm = llm;

        _yamlSerializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        _yamlDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task ExtractProperNounsAsync(string workingDir, string baseName, string inputFileName, int startChunk = 0, bool continuous = true)
    {
        var files = Utitilty.GetSortedPass1Files(workingDir, baseName);
        if (files == null || !files.Any())
        {
            SimpleLogger.LogCustom("[Pass 2] No node files found to scan.");
            return;
        }

        if (startChunk < 0 || startChunk >= files.Count)
        {
            SimpleLogger.LogCustom($"[Pass 2] Error: start chunk {startChunk} is out of bounds (0 ~ {files.Count - 1}).", ConsoleColor.Red);
            return;
        }

        int loopEnd = continuous ? files.Count - 1 : startChunk;

        Console.WriteLine("");
        Console.WriteLine($"[Pass 2] Starting ProperNoun Scan (Extraction & Rolling Translation) from chunk {startChunk} to {loopEnd}...");

        var rollingGlossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string outputPath = ProjectFiles.Pass2Glossary(workingDir);
        if (File.Exists(outputPath))
        {
            try
            {
                var yamlContent = File.ReadAllText(outputPath);
                var existing = _yamlDeserializer.Deserialize<Dictionary<string, string>>(yamlContent);
                if (existing != null)
                {
                    foreach (var kv in existing)
                    {
                        // 雖然已經不再產生 "===="，但為了相容舊版存檔，保留這個濾除邏輯
                        if (!kv.Key.StartsWith("====") && !string.IsNullOrWhiteSpace(kv.Value))
                        {
                            rollingGlossary[kv.Key] = kv.Value;
                        }
                    }
                }
                SimpleLogger.LogCustom($"[Pass 2] Loaded {rollingGlossary.Count} existing terms from history.");
            }
            catch (Exception ex)
            {
                SimpleLogger.LogCustom($"[Pass 2] Warning - Failed to load existing glossary: {ex.Message}", ConsoleColor.Yellow);
            }
        }

        for (int i = startChunk; i <= loopEnd; i++)
        {
            string currText = ExtractText(files[i]);

            var relevantGlossary = GetRelevantGlossary(currText, rollingGlossary);

            string yamlRef = "{}";
            if (relevantGlossary.Any())
            {
                yamlRef = _yamlSerializer.Serialize(relevantGlossary).TrimEnd().Replace("\n", "\n  ");
            }

            string systemPrompt = _llm.PrepareSystemPrompt(_promptConfig.ProperNounPrompt);

            string userPrompt = _promptConfig.ProperNounUserPrompt
                .Replace("<<currText>>", currText)
                .Replace("<<Glossary>>", yamlRef);

            try
            {
                string rawResponse = await _llm.CallLlmAsync(systemPrompt, userPrompt, $"\nStart propernoun scan chunk: {i} of {files.Count - 1}\n");

                string cleanYaml = CleanLlmYaml(rawResponse);

                var batchTerms = _yamlDeserializer.Deserialize<Dictionary<string, string>>(cleanYaml);

                if (batchTerms != null)
                {
                    foreach (var kvp in batchTerms)
                    {
                        string original = kvp.Key.Trim();
                        string translated = kvp.Value?.Trim() ?? "";

                        if (!currText.Contains(original, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!IsValidTerm(original, isSourceTerm: true) || !IsValidTerm(translated, isSourceTerm: false))
                            continue;

                        rollingGlossary[original] = translated;
                        if (!_frequencyMap.ContainsKey(original)) _frequencyMap[original] = 0;
                        _frequencyMap[original]++;
                    }
                }

                SimpleLogger.LogCustom($"[Pass 2] Chunk {i} of {files.Count - 1} : scan done.", ConsoleColor.White);
            }
            catch (Exception ex)
            {
                SimpleLogger.LogCustom($"[Pass 2] Warning - Format/Parsing error chunk {i} of {files.Count - 1}: {ex.Message}", ConsoleColor.Yellow);
            }

            Console.WriteLine("\n" + new string('-', 20));
        }

        SaveFinalGlossary(workingDir, rollingGlossary);

        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 2] Scan loop completed ", 60, '='));
    }

    private bool IsValidTerm(string term, bool isSourceTerm)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        term = term.Trim();

        char[] invalidChars = { '@', '#', '$', '%', '^', '=', '\\', '|', '~' };
        if (term.IndexOfAny(invalidChars) >= 0) return false;

        bool isAllAscii = term.All(c => c <= 127);
        if (isAllAscii && char.IsLower(term[0])) return false;

        if (!isSourceTerm) return true;

        if (term.Length <= 1) return false;

        if (Regex.IsMatch(term, @"^[\d\s_\!?#$%^*\/\\]+$")) return false;
        if (term.StartsWith('.')) return false;

        bool hasEnglish = Regex.IsMatch(term, @"[a-zA-Z]");

        if (hasEnglish)
        {
            if (Regex.IsMatch(term[0].ToString(), @"[a-zA-Z]") && !char.IsUpper(term[0]))
                return false;

            if (term.Contains(' '))
            {
                var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 4) return false;

                foreach (var p in parts)
                {
                    if (!string.IsNullOrEmpty(p) &&
                        Regex.IsMatch(p[0].ToString(), @"[a-zA-Z]") &&
                        !char.IsUpper(p[0]))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private Dictionary<string, string> GetRelevantGlossary(string currText, Dictionary<string, string> fullGlossary)
    {
        var relevant = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(currText)) return relevant;

        foreach (var kvp in fullGlossary)
        {
            string term = kvp.Key;
            if (string.IsNullOrWhiteSpace(term)) continue;

            bool isCjk = term.Any(c =>
                (c >= 0x4E00 && c <= 0x9FFF) ||
                (c >= 0x3400 && c <= 0x4DBF) ||
                (c >= 0x3040 && c <= 0x30FF) ||
                (c >= 0xAC00 && c <= 0xD7AF)
            );

            bool match = false;

            if (!isCjk)
            {
                var parts = term.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (currText.Contains(part, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }
            }
            else
            {
                if (term.Length < 2)
                {
                    if (currText.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                    }
                }
                else
                {
                    for (int i = 0; i <= term.Length - 2; i++)
                    {
                        string part = term.Substring(i, 2);
                        if (currText.Contains(part, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                    }
                }
            }

            if (match)
            {
                relevant[term] = kvp.Value;
            }
        }
        return relevant;
    }

    private void SaveFinalGlossary(string workingDir, Dictionary<string, string> rollingGlossary)
    {
        string outputPath = ProjectFiles.Pass2Glossary(workingDir);

        /* 【已註解】FuzzySharp 分群與分隔線邏輯
        var termsList = rollingGlossary.Keys.ToList();
        var clusteredTerms = TermClusterer.ClusterTerms(termsList, threshold: 80);
        clusteredTerms = clusteredTerms.OrderBy(cluster => cluster.First()).ToList();

        var finalGlossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int groupCounter = 1;

        foreach (var cluster in clusteredTerms)
        {
            string separator = $"===={groupCounter:D3}";
            finalGlossary[separator] = "";
            groupCounter++;

            foreach (var term in cluster.OrderBy(t => t))
            {
                finalGlossary[term] = rollingGlossary[term];
            }
        }
        */

        // 改為直接對所有的 Key 進行字母排序（這對 YAML 的人類可讀性已經足夠好了）
        var finalGlossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in rollingGlossary.Keys.OrderBy(t => t))
        {
            // 基本邏輯不變：過濾空字串不處理 (這在我們提取時已經保證過，但雙重防禦)
            if (!string.IsNullOrWhiteSpace(rollingGlossary[term]))
            {
                finalGlossary[term] = rollingGlossary[term];
            }
        }

        File.WriteAllText(outputPath, _yamlSerializer.Serialize(finalGlossary));

        int actualTermCount = finalGlossary.Count;

        Console.WriteLine("");
        Console.WriteLine($"[Pass 2] Processed {_frequencyMap.Count} terms total in this run.");
        // Console.WriteLine($"[Pass 2] Grouped {termsList.Count} terms into {clusteredTerms.Count} clusters via FuzzySharp."); // 【已註解】
        Console.WriteLine($"[Pass 2] Glossary saved to: {outputPath} (Actual terms: {actualTermCount})");
    }

    private string ExtractText(string filePath)
    {
        try
        {
            var yamlText = File.ReadAllText(filePath);
            var data = _yamlDeserializer.Deserialize<Pass1ChunkData>(yamlText);
            return data?.ResolvedText ?? "";
        }
        catch { return ""; }
    }

    private string CleanLlmYaml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "{}";

        var match = Regex.Match(input, @"```(?:yaml)?\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return input.Trim();
    }
}

/* 【已註解】保留 FuzzySharp 分群類別，供未來可能需要時解除註解
public static class TermClusterer
{
    public static List<List<string>> ClusterTerms(IEnumerable<string> terms, int threshold = 80)
    {
        var pending = terms.ToList();
        var clusters = new List<List<string>>();

        while (pending.Count > 0)
        {
            var currentCluster = new List<string>();

            var centerTerm = pending[0];
            var centerPreprocessed = PreprocessForFuzzy(centerTerm);

            currentCluster.Add(centerTerm);
            pending.RemoveAt(0);

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var candidate = pending[i];
                var candidatePreprocessed = PreprocessForFuzzy(candidate);

                var score = Fuzz.TokenSetRatio(centerPreprocessed, candidatePreprocessed);
                if (score >= threshold)
                {
                    currentCluster.Add(candidate);
                    pending.RemoveAt(i);
                }
            }
            clusters.Add(currentCluster);
        }

        return clusters;
    }

    private static string PreprocessForFuzzy(string input)
    {
        var spaced = Regex.Replace(input, @"([\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}])", " $1 ");
        return Regex.Replace(spaced, @"\s+", " ").Trim();
    }
}
*/