using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace HtmTxTrans;

// ==========================================
// 【新增】統一的專案檔案命名與路徑管理中心
// ==========================================
public static class ProjectFiles
{
    // 基礎結構檔案 (移除前綴，改為 .yaml 或 .html)
    public static string Skeleton(string dir) => Path.Combine(dir, "skeleton.html");
    public static string Metadata(string dir) => Path.Combine(dir, "metadata.yaml");
    public static string Hold(string dir) => Path.Combine(dir, "hold.yaml");
    public static string NodeAll(string dir) => Path.Combine(dir, "NodeAll.yaml");

    // Chunk 檔案 (Pass 1)
    public static string Pass0Chunk(string dir, int index) => Path.Combine(dir, $"Pass0.{index}.yaml");
    public static string Pass1Chunk(string dir, int index) => Path.Combine(dir, $"Pass1.{index}.yaml");

    // 預留未來 Pass 2 ~ 5 的命名空間，先寫好備用
    public static string Pass2Glossary(string dir) => Path.Combine(dir, "Pass2.glist.yaml");
    public static string Pass3Chunk(string dir, int index) => Path.Combine(dir, $"Pass3.{index}.yaml");
    public static string Pass4Chunk(string dir, int index) => Path.Combine(dir, $"Pass4.{index}.yaml");
    public static string Pass4Failure(string dir) => Path.Combine(dir, "Pass4.failure.yaml");
    public static string Pass5Chunk(string dir, int index) => Path.Combine(dir, $"Pass5.{index}.yaml");
    public static string Pass5Failure(string dir) => Path.Combine(dir, "Pass5.failure.yaml");
    public static string FinalChunk(string dir, int index) => Path.Combine(dir, $"Final.{index}.yaml");
}

public class AppConfig
{
    public string ApiEndpoint { get; set; } = "http://127.0.0.1:1234/v1";
    public string ApiKey { get; set; } = "no-key";
    public string ModelName { get; set; } = "gpt-oss-120b";
    public float Temperature { get; set; } = 0.05f;
    public float TopPValue { get; set; } = 0.05f;
    public int Seed { get; set; } = -1;
    public int DisableCachePrompt { get; set; } = 0;
    public int ApiTimeoutSeconds { get; set; } = 300;
    public int ApiWaitMSecond { get; set; } = 0;
    public int SocketErrorRetry { get; set; } = 2;
    public string TargetLanguage { get; set; } = "Traditional Chinese";
    public string ContentDomain { get; set; } = "Common";
    public int SlidingWindowSize { get; set; } = 500;
    public float SlidingWindowHardScale { get; set; } = 2.5f;
    public int Pass4RetryMax { get; set; } = 2;
    public int Pass5RetryMax { get; set; } = 2;

    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string The_Help { get; set; } = """
        HtmlTxTrans uses OpenAI or compatible LLM APIs for translation. Configuration options:

        ApiEndpoint: URL of the LLM API endpoint.
        (e.g., "http://localhost:1234/v1" for local servers, or the appropriate endpoint for cloud services)

        ApiKey: API key for authentication.
        (If not required, any value can be used)

        ModelName: Name of the LLM model.
        (e.g., "gpt-oss-120b", "gemini-3.1-flash". Ensure the model is available before use)

        Temperature: Controls output randomness.
        (Floating-point number 0 to 2. Recommended < 0.1 for more reproducible output)

        TopPValue: Controls nucleus sampling.
        (Floating-point number 0 to 1. Recommended < 0.1 for more reproducible output)

        Seed: Non-negative integer for reproducible output. Default = -1 (disabled).
        (If HTTP 400 occurs, the server may not support this; use -1 (Default))

        DisableCachePrompt: Set to 1 to disable cache_prompts for better reproducibility, but reduce performance.
        (If HTTP 400 occurs, the server may not support this; use 0 (Default))

        ApiTimeoutSeconds: Maximum time (in seconds) to wait for a response.
        (Set to 0 or negative to disable timeout; use with caution)

        ApiWaitMSecond: Wait time (in milliseconds) between API calls to avoid rate limits or server overload.
        (Default = 0, disable)

        SocketErrorRetry: Retries for API socket errors. The interval is 2 seconds.
        (Default = 2; set to 0 to disable. Aborts program when exhausted.)

        TargetLanguage: Target translation language.
        (e.g., "Traditional Chinese" or "繁體中文")

        ContentDomain: Content domain or field.
        (e.g., "Programming, Open source"; helps the model apply appropriate terminology and tone)

        SlidingWindowSize: Context chunk size for sliding window processing (soft estimate; includes overhead).
        (It may differ from expectations. Local LLM: recommended ≤ 500; Frontier cloud LLM: recommended ≥ 1500)

        SlidingWindowHardScale: Multiplier applied when no suitable split point is found (Hard limit).
        (Floating-point number. e.g., 500 × 2.5 = 1250)

        Pass4RetryMax: Maximum retries for Pass 4 (Node Alignment).
        (Default = 0, no retries)

        Pass5RetryMax: Maximum retries for Pass 5 (Tag Alignment).
        (Default = 0, no retries)
        """;
}

public class LlmPromptConfig
{
    public string _pass_1_a_p { get; set; } = "<================ Pass 1a: Sentence Boundary Detection ================>";
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string BoundaryDetectionPrompt { get; set; } = """
        You are an assistant that helps find the start of a complete sentence in a sequence of fragmented HTML text nodes.
        Task Instructions:
        1. Read the provided YAML array of nodes, each containing an 'Id' and 'Text'.
        2. Identify the 'Id' of the node that most likely represents the BEGINNING of a new, complete sentence or logical block.
        3. If no such node exists, or if the text is entirely fragmented UI elements without a clear sentence start, return -1.
        4. NO think, NO explain. Output ONLY the raw integer ID (e.g., 10 or -1). Do NOT output YAML or any other text.
        """;

    public string _pass_1_b_p { get; set; } = "<================ Pass 1b: SEP Resolution ================>";
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string SepResolutionSystemPrompt { get; set; } = """
        Objective: Reconstruct the original text by resolving all '<SEP>' tags.

        Step 1: Evaluate each '<SEP>' tag contextually
        - If it splits a single word or logical entity, remove it.
        - If it separates distinct UI elements, labels, or independent phrases, replace it with a space (" ").
        - If ambiguity remains after analysis, default to a space.
        - Otherwise, determine the appropriate action based on natural syntax.

        Step 2: Output
        - Return ONLY the reconstructed source text with all '<SEP>' tags resolved.
        - Do NOT translate or convert '<SEP>' into any other form (e.g., punctuation).
        - Do NOT include any additional text, formatting, or explanation.
        """;

    public string _pass_2_p { get; set; } = "<================ Pass 2: Proper Noun Extraction ================>";
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string ProperNounPrompt { get; set; } = """
        Objective: Analyze only the text within "ExtractContent" field , which belongs to the '<<Domain>>' domain. Identify terms that are conventionally transliterated into '<<Target>>'.

        Note on "Transliteration":
        Here, transliteration means phonetic rendering—mapping the original term’s pronunciation into '<<Target>>' characters, not translating its literal meaning.

        Step 1: Source & Reference
        - Content Source: ONLY process text found inside the "ExtractContent" field tag.
        - Terminology Reference: Provided in the "ReferenceGlossary" field tag.

        Step 2: Selection Criteria (Mandatory Conditions)
        [A term is VALID ONLY IF it satisfies ALL of the following conditions.]
        - It is a clear named entity (person, character, place, organization, or brand), NOT a general technical term or abbreviation.
        - It is clearly and conventionally transliterated into '<<Target>>' in the '<<Domain>>' domain, with widespread real-world usage (not rare, theoretical, or inferred forms).
        [AND does NOT match ANY of the following conditions
        - Pure numbers or mixed with symbols (e.g., date, time, currency, formula).
        - Fragmented UI text.
        - Code, CLI commands, URLs, or file paths.
        - Generic words or capitalized common phrases (e.g., "Next Step").
        - Model names, or technical terms not typically transliterated in '<<Target>>'.
        - Abbreviations/acronyms unless they are well-known named entities.
        [Preprocessing before Extraction]
        - Honorifics and titles are not a part of named entity. Remove theme before extract.
        - Remove parentheses and their contents.

        Step 3: Processing & Localization Rules
        - Do not use explanations in place of translation (e.g., NOT "Copilot (程式碼助理)").
        - Apply the following rules in order, until a condition is met. For each extracted term, apply the following process in the '<<Domain>>' domain for '<<Target>>' localization:
        1. Reference glossary in "ReferenceGlossary" if available.
        2. If the glossary is not available, use the standard, mainstream, or widely accepted form.
        3. If no standard form exists and the term is commonly kept in its original form, keep it unchanged.
        4. If no standard form exists and the term is commonly transliterated, perform pure phonetic transliteration.

        Step 4: Formatting & Output Requirements
        - Output Format: Return ONLY a valid YAML object mapping the original terms to their localized forms. 
          Example: 
          OriginalTerm1: TranslatedTerm1
          OriginalTerm2: TranslatedTerm2
        - Encoding: Output actual Unicode characters; do NOT use escape sequences (e.g., \uXXXX).
        - If no valid terms are found, output an empty mapping ({}). 
        - Do not output any extra text or explanations.
        """;

    public string _pass_2_u { get; set; } = "<================ Pass 2: Proper Noun Extraction User context ================>";
    public string ProperNounUserPrompt { get; set; } = """
        ExtractContent: >
          <<currText>>
        ReferenceGlossary:
          <<Glossary>>
        """;

    public string _pass_3_p { get; set; } = "<================ Pass 3: Context-Aware Translation ================>";
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string TranslationSystemPrompt { get; set; } = """
        Objective: Extensive knowledge in the '<<Domain>>' domain, providing precise translations into '<<Target>>'.

        Step 1: Analyze context
        - Read the "DocumentStream" field. Use the "TextBefore" and "TextAfter" fields as sequential context for background understanding.

        Step 2: Apply glossary
        - Consult the "Glossary" field. For each mapping, strictly map every occurrence of the proper noun with its corresponding target.

        Step 3: Translation requirements
        - Strictly translate ONLY the text within the "TargetToTranslate" field into '<<Target>>'.
        - Non-literary: Ensure 100% semantic coverage and strict technical accuracy.
        - Literary/Creative: Use a fluent, natural tone and artistic style suitable for the genre. Avoid "translationese"; Restructure sentences if necessary to sound native.
        - There may be unexpected extra spaces in the text. Please reasonably ignore these spaces when translating.
        - Zero Omission: Even mixed languages, fragments, tags, header, and UI labels must be translated into the target language, unless they are specified in the glossary.
        - Translate footnotes and citation markers into the target language with original structure.
        - Technical terms: Translate them as much as possible, unless no reasonable equivalent exists in the "<<Target>>" language.
        - Icons/Emojis (including unicode escapes): Copy them exactly. They act as anchors; Do not move or remove them. 
        - Do not translate text that is clearly functional code, CLI command, URL, or file path.

        Step 4: Finalize Output
        - Output ONLY the final translated text. Do not output any formatting or explanations.
        """;

    public string _pass_3_u { get; set; } = "<================ Pass 3: Context-Aware Translation User context ================>";
    public string TranslationUserPrompt { get; set; } = """
        DocumentStream:
          TextBefore: >
            <<preContext>>
          TargetToTranslate: >
            <<currentContext>>
          TextAfter: >
            <<nextContext>>
        Glossary:
          <<Glossary>>
        """;

    // ==========================================
    // Pass 4
    // ==========================================
    public string _pass_4_p { get; set; } = "<================ Pass 4: Node Alignment ================>";
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string AlignmentSystemPrompt { get; set; } = """
        Objective: Perform an IN-PLACE UPDATE on the "CurrentNodes" field. Replace the "Text" value of each object with its corresponding translated fragment from the "TranslatedText" field.

        Step 1: Contextualize
        - Read the "OriginalText" and "TranslatedText" fields to fully understand the semantic mapping.

        Step 2: Match & update values
        - Iterate through every object in the "CurrentNodes" field. Find its corresponding translated fragment in the "TranslatedText" field and replace the "Text" value with it.
        - There may be unexpected extra spaces in the text. Please reasonably ignore these spacing differences when matching.
        - If a "Text" is empty or only spaces, skip updating it and leave it unchanged.

        Step 3: Strict structural integrity
        - The input and output "Id" values MUST remain exactly identical. Do not add, remove, or modify any ID.
        - You are only updating the "Text" fields. The total number of objects and the overall YAML array structure must remain completely unchanged.
        - If there is nothing to update doesn't mean the Id can be omitted.

        Step 4: Finalize Output
        - Output ONLY the raw updated YAML array for the nodes (i.e., starting with "- Id:").
        - Do not include the "CurrentNodes" parent key, and do not include any explanations.
        """;

    public string _pass_4_u { get; set; } = "<================ Pass 4: Node Alignment User context ================>";
    public string AlignmentUserPrompt { get; set; } = """
        OriginalText: >
          <<OriginalText>>
        TranslatedText: >
          <<TranslatedText>>
        CurrentNodes:
          <<CurrentJson>>
        """;

    // ==========================================
    // Pass 5
    // ==========================================
    public string _pass_5_p { get; set; } = "<================ Pass 5: Tag Alignment ================>";
    [YamlMember(ScalarStyle = ScalarStyle.Literal)]
    public string TagAlignmentSystemPrompt { get; set; } = """
        Objective: Perform an IN-PLACE UPDATE on the "NodeAlignedNodes" field. Insert the exact '<x_n>' tags into its "Text" values by referencing the tag positions from the "OriginalNodes" field.

        Step 1: Set the workspace and reference
        - Treat the "NodeAlignedNodes" field as your strict workspace. The YAML structure, object count, and "Id" values MUST remain exactly identical.
        - Treat the "OriginalNodes" field purely as a read-only reference to find where the '<x_n>' tags belong.

        Step 2: Transfer & position tags
        - For every object, insert all original '<x_n>' tags into the translated "Text" field at their correct semantic positions.
        - Direct Mapping: Match tags to the translated equivalent. (e.g., "Click <x_0>here<x_1>" → "點擊<x_0>這裡<x_1>")
        - Merged/Lost Words: DO NOT delete tags. If a tagged word is merged or omitted during translation, stack the tags together at the closest logical position. (e.g., "<x_3>Does<x_4> he know?" → "<x_3><x_4>他知道嗎?")

        Step 3: Strict tag verification
        - You MUST NOT delete, alter, or fabricate any '<x_n>' tags. 
        - The exact number and IDs of tags in your updated text MUST perfectly match the original text for each node.
        - Do NOT alter the translated text itself; ONLY insert the tags.

        Step 4: Finalize Output
        - Output ONLY the raw updated YAML array for the nodes (i.e., starting with "- Id:").
        - Do not include the "NodeAlignedNodes" parent key, and do not include any explanations.
        """;
    public string _pass_5_u { get; set; } = "<================ Pass 5: Tag Alignment User context ================>";
    public string TagAlignmentUserPrompt { get; set; } = """
        OriginalNodes:
          <<CurrentJson>>
        NodeAlignedNodes:
          <<NodeAlignedJson>>
        """;
}

public class NodeEntry
{
    public int Id { get; set; }

    // 強制 YamlDotNet 在序列化這個字串時，永遠加上雙引號
    [YamlMember(ScalarStyle = ScalarStyle.DoubleQuoted)]
    public string Text { get; set; } = "";
}

public class Pass1ChunkData
{
    public string ResolvedText { get; set; } = "";
}

public class ProjectMetadata
{
    public int AttributeIdBoundary { get; set; } = -1;
    public int TotalIdsUpper { get; set; } = -1;
    public int TotalFilesUpper { get; set; } = -1;

    public static ProjectMetadata? LoadMetadata(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            // 改用 YamlDotNet 來讀取
            var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
            return deserializer.Deserialize<ProjectMetadata>(File.ReadAllText(path));
        }
        catch { return null; }
    }
}

public class HoldEntry
{
    public string Placeholder { get; set; } = "";
    public string Content { get; set; } = "";
}

public static class Utitilty
{
    public static string PadCenterCustom(string input, int totalWidth, char paddingChar = '=')
    {
        if (string.IsNullOrEmpty(input)) return new string(paddingChar, totalWidth);

        int visualWidth = 0;
        foreach (char c in input)
        {
            visualWidth += (c > 127) ? 2 : 1;
        }

        if (visualWidth >= totalWidth) return input;

        int totalPaddingNeeded = totalWidth - visualWidth;
        int leftPadding = totalPaddingNeeded / 2;
        int rightPadding = totalPaddingNeeded - leftPadding;

        return new string(paddingChar, leftPadding) + input + new string(paddingChar, rightPadding);
    }

    // 參數保留 baseName 以相容 Program.cs 的呼叫，但內部已改用統一的 ProjectFiles
    public static List<string> GetSortedPass1Files(string workingDir, string baseName)
    {
        string metadataPath = ProjectFiles.Metadata(workingDir);
        var metadata = ProjectMetadata.LoadMetadata(metadataPath);

        if (metadata == null || metadata.TotalFilesUpper < 0)
        {
            SimpleLogger.LogCustom($"[Warning] Metadata read error - File not found or unable to be parsed: {metadataPath}", ConsoleColor.Red);
            return new List<string>();
        }

        int totalFiles = metadata.TotalFilesUpper;
        var files = new List<string>();

        for (int i = 0; i <= totalFiles; i++)
        {
            files.Add(ProjectFiles.Pass1Chunk(workingDir, i));
        }

        return files;
    }
    public static string SerializeYamlToUtf8(this YamlDotNet.Serialization.ISerializer serializer, object data)
    {
        // 1. 先讓 YamlDotNet 正常序列化 (這時 Emoji 會變成 \U0001F451)
        string yaml = serializer.Serialize(data);

        // 2. 攔截並還原 8 碼 Unicode (Emojis，例如 \U0001F451)
        // (?<!\\) 是防呆機制：確保如果原文真的打了字元 '\' + 'U'，我們不會誤殺它
        yaml = System.Text.RegularExpressions.Regex.Replace(yaml, @"(?<!\\)\\U([0-9A-Fa-f]{8})",
            m => char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));

        // 3. 攔截並還原 4 碼 Unicode (某些中日韓罕見字或符號，例如 \u4E2D)
        yaml = System.Text.RegularExpressions.Regex.Replace(yaml, @"(?<!\\)\\u([0-9A-Fa-f]{4})",
            m => char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));

        return yaml;
    }
}

public static class ContextHelper
{
    public static string GetSmartPastContext(string text, int targetLength = 50)
    {
        if (targetLength == 0) return "";
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (text.Length <= targetLength) return text;

        int startIndex = text.Length - targetLength;

        while (startIndex > 0 && !IsBoundary(text[startIndex - 1]))
        {
            startIndex--;
        }

        string result = text.Substring(startIndex).TrimStart();
        return startIndex > 0 ? "..." + result : result;
    }

    public static string GetSmartNextContext(string text, int targetLength = 25)
    {
        if (targetLength == 0) return "";
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (text.Length <= targetLength) return text;

        int endIndex = targetLength;

        while (endIndex < text.Length && !IsBoundary(text[endIndex]))
        {
            endIndex++;
        }

        if (endIndex < text.Length - 1)
        {
            return text.Substring(0, endIndex + 1).TrimEnd() + "...";
        }

        return text;
    }

    private static bool IsBoundary(char c)
    {
        return char.IsWhiteSpace(c) ||
               char.IsPunctuation(c) ||
               c == '　' ||
               "。，！？、；：「」『』（）<>＜＞【】《》“”※…".Contains(c);
    }
}

public static class SimpleLogger
{
    public static string LogFile { get; set; } = string.Empty;
    private static readonly object _lockObj = new object();

    public static void LogCustom(string message, ConsoleColor fgColor = ConsoleColor.Gray, ConsoleColor bgColor = ConsoleColor.Black)
    {
        lock (_lockObj)
        {
            Console.ForegroundColor = fgColor;
            Console.BackgroundColor = bgColor;
            Console.WriteLine("");

            if (!string.IsNullOrWhiteSpace(LogFile))
            {
                Console.Write($"(Logged) ");
                try
                {
                    string fileContent = $"[{DateTime.Now:yyyy-MM-dd} - {DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFile, fileContent);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Logger Error] 無法寫入日誌檔: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine($"{message}");
            Console.ResetColor();
        }
    }
}