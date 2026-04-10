using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HtmTxTrans;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ==========================================
// 建立 YAML 序列化與反序列化器
// ==========================================
var yamlSerializer = new SerializerBuilder()
    .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
    .Build();

var yamlDeserializer = new DeserializerBuilder()
    .IgnoreUnmatchedProperties()
    .Build();

// ==========================================
// 1. 先檢查直接執行的命令 (--help, --sy, --sp)
// ==========================================
if (args.Contains("--help"))
{
    DisplayHelp();
    return 0;
}

if (args.Contains("--sc"))
{
    Console.WriteLine(yamlSerializer.Serialize(new AppConfig()));
    return 0;
}

if (args.Contains("--sp"))
{
    // 取得 YamlDotNet 序列化出來的原始字串
    string rawYaml = yamlSerializer.Serialize(new LlmPromptConfig());

    // 統一換行符號為 \n，方便處理
    string normalizedYaml = rawYaml.Replace("\r\n", "\n");

    // 1. 處理上方空行：在偽 Key 的「上方」插入一個空行
    // 尋找換行符號緊接著 _pass_ 的地方，多塞一個換行
    string formattedYaml = normalizedYaml.Replace("\n_pass_", "\n\n_pass_");

    // 2. 處理下方空行：在分隔線結尾的「下方」插入一個空行
    // 因為 YamlDotNet 可能會把字串加上單引號 (') 或雙引號 (")，
    // 我們尋找 ====>' 或者 ====>" 或者單純的 ====>，並在它後面的換行符號前多塞一個換行。
    formattedYaml = formattedYaml.Replace("====>'\n", "====>'\n\n")
                                 .Replace("====>\"\n", "====>\"\n\n")
                                 .Replace("====>\n", "====>\n\n");

    // 寫回控制台，並將換行符號轉回當前系統預設
    Console.WriteLine(formattedYaml.Replace("\n", Environment.NewLine));
    return 0;
}

// ==========================================
// 建立標記陣列，用於檢查未識別的參數
// ==========================================
bool[] handledArgs = new bool[args.Length];

bool CheckFlag(string flag)
{
    int index = Array.IndexOf(args, flag);
    if (index >= 0)
    {
        handledArgs[index] = true;
        return true;
    }
    return false;
}

string? GetStringValue(string flag)
{
    int index = Array.IndexOf(args, flag);
    if (index >= 0)
    {
        handledArgs[index] = true;
        if (index + 1 < args.Length && !args[index + 1].StartsWith("-"))
        {
            handledArgs[index + 1] = true;
            return args[index + 1];
        }
    }
    return null;
}

// ==========================================
// 2. 擷取所有參數與標記
// ==========================================
string? settingsPath = GetStringValue("-c");
string? inputPath = GetStringValue("-i");
string? promptsPath = GetStringValue("-p");
string? workingDirectory = GetStringValue("-w");

bool hasLogFlag = CheckFlag("--log");

if (hasLogFlag)
{
    SimpleLogger.LogFile = "LogOutput.txt";
}

// ==========================================
// 處理帶有選擇性後綴的 --p1 [n|f]
// ==========================================
bool p1 = false;
bool p1n = false;
bool p1f = false;

int p1Idx = Array.IndexOf(args, "--p1");
if (p1Idx >= 0)
{
    p1 = true;
    handledArgs[p1Idx] = true;
    if (p1Idx + 1 < args.Length && !args[p1Idx + 1].StartsWith("-"))
    {
        handledArgs[p1Idx + 1] = true;
        string val = args[p1Idx + 1].ToLower();
        if (val.Contains("n")) p1n = true;
        if (val.Contains("f")) p1f = true;
    }
}

bool p1p = CheckFlag("--p1p");

// ==========================================
// 處理帶有選擇性數值與後綴的 --p2, --p3, --p4, --p5
// ==========================================
bool p2 = false, p2Continuous = true; int p2Chunk = 0;
int p2Idx = Array.IndexOf(args, "--p2");
if (p2Idx >= 0)
{
    p2 = true; handledArgs[p2Idx] = true;
    if (p2Idx + 1 < args.Length && !args[p2Idx + 1].StartsWith("-"))
    {
        handledArgs[p2Idx + 1] = true; string val = args[p2Idx + 1];
        if (val.EndsWith("+")) { p2Continuous = true; val = val.Substring(0, val.Length - 1); } else { p2Continuous = false; }
        if (int.TryParse(val, out int chunk)) p2Chunk = chunk; else return 1;
    }
}
bool p2x = CheckFlag("--p2x");

bool p3 = false, p3Continuous = true; int p3Chunk = 0;
int p3Idx = Array.IndexOf(args, "--p3");
if (p3Idx >= 0)
{
    p3 = true; handledArgs[p3Idx] = true;
    if (p3Idx + 1 < args.Length && !args[p3Idx + 1].StartsWith("-"))
    {
        handledArgs[p3Idx + 1] = true; string val = args[p3Idx + 1];
        if (val.EndsWith("+")) { p3Continuous = true; val = val.Substring(0, val.Length - 1); } else { p3Continuous = false; }
        if (int.TryParse(val, out int chunk)) p3Chunk = chunk; else return 1;
    }
}

bool p4 = false, p4Continuous = true; int p4Chunk = 0;
int p4Idx = Array.IndexOf(args, "--p4");
if (p4Idx >= 0)
{
    p4 = true; handledArgs[p4Idx] = true;
    if (p4Idx + 1 < args.Length && !args[p4Idx + 1].StartsWith("-"))
    {
        handledArgs[p4Idx + 1] = true; string val = args[p4Idx + 1];
        if (val.EndsWith("+")) { p4Continuous = true; val = val.Substring(0, val.Length - 1); } else { p4Continuous = false; }
        if (int.TryParse(val, out int chunk)) p4Chunk = chunk; else return 1;
    }
}

bool p5 = false, p5Continuous = true; int p5Chunk = 0;
int p5Idx = Array.IndexOf(args, "--p5");
if (p5Idx >= 0)
{
    p5 = true; handledArgs[p5Idx] = true;
    if (p5Idx + 1 < args.Length && !args[p5Idx + 1].StartsWith("-"))
    {
        handledArgs[p5Idx + 1] = true; string val = args[p5Idx + 1];
        if (val.EndsWith("+")) { p5Continuous = true; val = val.Substring(0, val.Length - 1); } else { p5Continuous = false; }
        if (int.TryParse(val, out int chunk)) p5Chunk = chunk; else return 1;
    }
}

bool p4r = CheckFlag("--p4r");
bool p5r = CheckFlag("--p5r");
bool p6 = CheckFlag("--p6");

// ==========================================
// 3. 終極防線：檢查未標記參數
// ==========================================
var unknownArgs = args.Where((arg, index) => !handledArgs[index]).ToList();
if (unknownArgs.Any())
{
    Console.WriteLine($"[Error] Unrecognized or invalid arguments: {string.Join(" ", unknownArgs)}\n");
    DisplayHelp();
    return 1;
}

// ==========================================
// 4. 驗證必要參數與互斥邏輯
// ==========================================
if (string.IsNullOrEmpty(settingsPath) || string.IsNullOrEmpty(inputPath))
{
    Console.WriteLine("[Error] Syntax Error: -c and -i must be provided together.\n");
    DisplayHelp();
    return 1;
}

if (!File.Exists(settingsPath)) { Console.WriteLine($"[Error] Config file does not exist: {settingsPath}"); return 1; }
if (!File.Exists(inputPath)) { Console.WriteLine($"[Error] Input HTML file does not exist: {inputPath}"); return 1; }

int exclusiveCount = 0;
if (p1) exclusiveCount++;
if (p2) exclusiveCount++;
if (p2x) exclusiveCount++;
if (p3) exclusiveCount++;
if (p4) exclusiveCount++;
if (p5) exclusiveCount++;
if (p6) exclusiveCount++;
if (p4r) exclusiveCount++;
if (p5r) exclusiveCount++;

if (exclusiveCount > 1)
{
    Console.WriteLine("\n[Error] Syntax Error: Cannot combine multiple specific pass arguments. Please specify only one.\n");
    return 1;
}

bool runFullWorkflow = (exclusiveCount == 0) || p2x;

bool runPass1 = p1 || runFullWorkflow;
bool runPass2 = p2 || (runFullWorkflow && !p2x);
bool runPass3 = p3 || runFullWorkflow;
bool runPass4 = p4 || runFullWorkflow;
bool runPass5 = p5 || runFullWorkflow;
bool runPass6 = p6 || runFullWorkflow;

bool runPass4Retry = p4r || (runPass4 && p4Continuous);
bool runPass5Retry = p5r || (runPass5 && p5Continuous);

// ==========================================
// 讀取設定檔與初始化
// ==========================================
AppConfig config;
try
{
    string configYaml = await File.ReadAllTextAsync(settingsPath);
    config = yamlDeserializer.Deserialize<AppConfig>(configYaml) ?? new AppConfig();
}
catch (Exception ex)
{
    Console.WriteLine($"[Error] Reading config YAML failed: {ex.Message}");
    return 1;
}

LlmPromptConfig promptConfig = new LlmPromptConfig();
if (!string.IsNullOrEmpty(promptsPath))
{
    if (!File.Exists(promptsPath)) { Console.WriteLine($"[Error] Prompts file does not exist: {promptsPath}"); return 1; }
    try
    {
        string promptYaml = await File.ReadAllTextAsync(promptsPath);
        promptConfig = yamlDeserializer.Deserialize<LlmPromptConfig>(promptYaml) ?? new LlmPromptConfig();
    }
    catch (Exception ex) { Console.WriteLine($"[Error] Reading prompts YAML failed: {ex.Message}"); return 1; }
}

string workingDir = !string.IsNullOrWhiteSpace(workingDirectory)
    ? workingDirectory
    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{Path.GetFileName(inputPath)}.working");

if (!Directory.Exists(workingDir))
{
    Directory.CreateDirectory(workingDir);
}

string baseFileName = "Work";
string inputFileNameOnly = Path.GetFileName(inputPath) ?? "UnknownFile.html";

var llmService = new LlmService(config);

// ==========================================
// 核心工作流程執行
// ==========================================
try
{
    SimpleLogger.LogCustom($"<HtmTxTrans> Start with: " + String.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg)));

    if (runPass1)
    {
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 1] HTML Extraction & SEP Resolution ", 60, '='));
        Console.WriteLine("");
        var processor = new HtmlProcessor(config, promptConfig, llmService, !p1n, p1f, p1p);
        await processor.ExtractAsync(inputPath, workingDir, config.SlidingWindowSize);
        if (p1) return 0;
    }

    if (runPass2)
    {
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 2] Proper Noun Extraction & Translation ", 60, '='));
        var scanner = new ProperNounScanner(promptConfig, llmService);
        await scanner.ExtractProperNounsAsync(workingDir, baseFileName, inputFileNameOnly, p2Chunk, p2Continuous);
        if (p2) return 0;
    }

    if (runPass3)
    {
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 3] Context-Aware Translation ", 60, '='));
        var translator = new TranslationScanner(config, promptConfig, llmService);
        await translator.TranslateToTextAsync(workingDir, baseFileName, inputFileNameOnly, p3Chunk, p3Continuous);
    }

    if (runPass4)
    {
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 4] Node Alignment ", 60, '='));
        var translator = new TranslationScanner(config, promptConfig, llmService);
        await translator.NodeAlignTranslationAsync(workingDir, baseFileName, inputFileNameOnly, p4Chunk, p4Continuous);
    }

    if (runPass4Retry)
    {
        if (p4r) SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 4 Retry] Standalone Mode ", 60, '='));
        var translator = new TranslationScanner(config, promptConfig, llmService);
        await translator.RetryNodeAlignAsync(workingDir, baseFileName, inputFileNameOnly);
        if (p4 || p4r) return 0;
    }

    if (runPass5)
    {
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 5] Tag Alignment ", 60, '='));
        var translator = new TranslationScanner(config, promptConfig, llmService);
        await translator.TagAlignTranslationAsync(workingDir, baseFileName, inputFileNameOnly, p5Chunk, p5Continuous);
    }

    if (runPass5Retry)
    {
        if (p5r) SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 5 Retry] Standalone Mode ", 60, '='));
        var translator = new TranslationScanner(config, promptConfig, llmService);
        await translator.RetryTagAlignAsync(workingDir, baseFileName, inputFileNameOnly);
        if (p5 || p5r) return 0;
    }

    if (runPass6)
    {
        SimpleLogger.LogCustom(Utitilty.PadCenterCustom(" [Pass 6] HTML Restoration ", 60, '='));
        Console.WriteLine("");
        var restorer = new HtmlRestorer();
        string inputFileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        string extension = Path.GetExtension(inputPath);
        string outputFileName = $"{inputFileNameWithoutExt}.Translated{extension}";
        restorer.Restore(inputPath, workingDir, outputFileName);
    }
}
catch (Exception ex)
{
    SimpleLogger.LogCustom($"\n[Error] Process failed: {ex.Message}", ConsoleColor.Red);
    return 1;
}

return 0;

static void DisplayHelp()
{
    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
    Console.WriteLine($"""
    HtmTxTrans CLI v{version}
    --------------------------------------
    Basic Usage:

      HtmTxTrans -c config.yaml -i input.html [-p prompts.yaml] [-w working_dir] [--log] [--px...]

    Basic Options:

      -c  [file] : Path to YAML config file (required for main workflow).
      -i  [file] : Path to input HTML file (required for main workflow).
      -p  [file] : (optional) Path to LLM prompts YAML file.
      -w  [dir]  : (optional) Specify working directory (Default = input_file_name.working).
      --log      : (optional) Enable logging to file LogOutput.txt.
      --px       : (optional) Workflow control options (see below).

    Generate config / prompts samples:

      HtmTxTrans --sc > config.yaml
      HtmTxTrans --sp > prompts.yaml

    Workflow control options (Only one pass a time. Runs full workflow if omitted):

      Pass 1 (Extract HTML nodes & Resolve SEP):

        --p1 [n|f]  : Run Pass 1 ONLY.
                      'n' to disable LLM assistance for hard-limit chunk splitting.
                      'f' to disable LLM <SEP> resolution (use spaces instead).
                      (Options can be combined, e.g., --p1 nf)
        --p1p       : Enable translation for <pre> tags.

      Pass 2 (Extract proper nouns & generate glossary):

        --p2 [n|n+] : Run Pass 2 ONLY. Extract and translate proper nouns continuously.
                      Specify 'n' for a single chunk, or 'n+' to process continuously.
        --p2x       : Run full workflow but Skip Pass 2.

      Pass 3 (Translate nodes):

        --p3 [n|n+] : Run Pass 3 ONLY. Specify 'n' for a single chunk, or 'n+' to process continuously from chunk 'n'.

      Pass 4 (Node Alignment):

        --p4 [n|n+] : Run Pass 4 ONLY. Specify 'n' for a single chunk, or 'n+' to process continuously from chunk 'n'.
        --p4r       : Run Pass 4 Retry ONLY (Stand-alone mode for failures).

      Pass 5 (Tag Alignment):

        --p5 [n|n+] : Run Pass 5 ONLY. Specify 'n' for a single chunk, or 'n+' to process continuously from chunk 'n'.
        --p5r       : Run Pass 5 Retry ONLY (Stand-alone mode for failures).

      Pass 6 (Restore HTML):

        --p6        : Run Pass 6 ONLY.

    """);
}