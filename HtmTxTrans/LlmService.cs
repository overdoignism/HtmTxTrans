using OpenAI;
using OpenAI.Chat;
using System.Text.RegularExpressions;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace HtmTxTrans;

public class LlmService
{
    private readonly ChatClient _client;
    private readonly AppConfig _config;

    public LlmService(AppConfig config)
    {
        _config = config;

        TimeSpan timeout = config.ApiTimeoutSeconds <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(config.ApiTimeoutSeconds);

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(config.ApiEndpoint),
            NetworkTimeout = timeout,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };

        if (_config.DisableCachePrompt == 1)
        {
            options.AddPolicy(new DisableKvCachePolicy(), PipelinePosition.BeforeTransport);
        }

        var apiSecret = new ApiKeyCredential(config.ApiKey);
        var openAiClient = new OpenAIClient(apiSecret, options);
        _client = openAiClient.GetChatClient(config.ModelName);
    }

    public async Task<string> CallLlmAsync(string systemPrompt, string userContent, string initialString)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(initialString);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[LLM Request]");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("System Prompt:");
        Console.ResetColor();
        Console.WriteLine($"{systemPrompt}");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\nUser Content:");
        Console.ResetColor();
        Console.WriteLine($"{userContent}");

        ChatCompletionOptions options = new ChatCompletionOptions
        {
            Temperature = _config.Temperature,
            TopP = _config.TopPValue,
        };

        if (_config.Seed != -1)
        {
#pragma warning disable OPENAI001
            options.Seed = _config.Seed;
#pragma warning restore OPENAI001
        }

        // 把 Retry 機制搬到這裡
        int maxRetries = Math.Max(0, _config.SocketErrorRetry);
        int currentAttempt = 0;

        while (true)
        {
            try
            {
                if (_config.ApiWaitMSecond > 0)
                {
                    await Task.Delay(_config.ApiWaitMSecond);
                }

                ChatCompletion completion = await _client.CompleteChatAsync(new ChatMessage[]
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userContent)
                }, options);

                string response = completion.Content[0].Text;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[LLM Response]");
                Console.ResetColor();
                Console.WriteLine(response);

                return DirtyFilter(response);
            }
            catch (Exception ex)
            {
                currentAttempt++;
                if (currentAttempt > maxRetries)
                {
                    // 超過次數，直接往外拋出例外中斷整個程式
                    throw new Exception($"LLM API Socket Error exceeded maximum retries ({maxRetries}). Last error: {ex.Message}", ex);
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[API Error] {ex.Message}. Retrying in 2 seconds ({currentAttempt}/{maxRetries})...");
                Console.ResetColor();
                await Task.Delay(2000);
            }
        }
    }

    public string PrepareSystemPrompt(string template, string glossaryJson = "")
    {
        return template
            .Replace("<<Domain>>", _config.ContentDomain)
            .Replace("<<Target>>", _config.TargetLanguage)
            .Replace("<<Glossary>>", glossaryJson);
    }

    public static string DirtyFilter(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return "";
        string cleanedResponse = Regex.Replace(rawResponse, @"(?si)(<think>.*?</think>|\[think\].*?\[/think\])", "");
        cleanedResponse = Regex.Replace(cleanedResponse.Trim(), @"^```[a-zA-Z]*\n?(.*?)\n?```$", "$1", RegexOptions.Singleline).Trim();
        cleanedResponse = cleanedResponse.Trim('`').Trim();
        return cleanedResponse;
    }
}

public class DisableKvCachePolicy : PipelinePolicy
{ /* ... 保持不變 ... */
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) { InjectCachePrompt(message); ProcessNext(message, pipeline, currentIndex); }
    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) { InjectCachePrompt(message); await ProcessNextAsync(message, pipeline, currentIndex); }
    private void InjectCachePrompt(PipelineMessage message)
    {
        if (message.Request.Method == "POST" && message.Request.Uri.AbsolutePath.Contains("/chat/completions") && message.Request.Content != null)
        {
            using var stream = new MemoryStream();
            message.Request.Content.WriteTo(stream, message.CancellationToken);
            stream.Position = 0;
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode is JsonObject jsonObj)
            {
                jsonObj["cache_prompt"] = false;
                string newJson = jsonObj.ToJsonString();
                var bytes = System.Text.Encoding.UTF8.GetBytes(newJson);
                message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(bytes));
                message.Request.Headers.Set("Content-Length", bytes.Length.ToString());
            }
        }
    }
}