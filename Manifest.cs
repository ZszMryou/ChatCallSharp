using MinorShift.Emuera.Runtime.Utils.PluginSystem;
using System;
using System.Windows;
using System.Windows.Forms;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
namespace ChatCallSharp
{
    //class MUST be named PluginManifest
    public class PluginManifest : PluginManifestAbstract
    {
        public PluginManifest()
        {
            methods.Add(new ModelSetMethod());
            methods.Add(new ChatMethod());
            methods.Add(new ChatExMethod());
            methods.Add(new ChatStreamMethod());
            methods.Add(new ChatJsonMethod());
            methods.Add(new HttpGetJsonMethod());
            methods.Add(new ChatRawMethod());
            methods.Add(new LastStatusMethod());
            methods.Add(new LastErrorMethod());
            methods.Add(new JsonGetMethod());
            methods.Add(new JsonGetRawMethod());
            methods.Add(new JsonGetIntMethod());
            methods.Add(new JsonGetBoolMethod());
            methods.Add(new JsonHasMethod());
            methods.Add(new JsonTypeMethod());
            methods.Add(new JsonCountMethod());
            methods.Add(new JsonValidMethod());
            methods.Add(new JsonCompactMethod());
            methods.Add(new JsonPrettyMethod());
            methods.Add(new JsonEncodeMethod());
            methods.Add(new JsonDecodeMethod());
            methods.Add(new RegexGetMethod());
            methods.Add(new SplitGetMethod());
            methods.Add(new BetweenMethod());
            methods.Add(new ReplaceMethod());
            methods.Add(new TestBuiltinFunctions());
        }

        public override string PluginName => "ChatCallSharp";

        public override string PluginDescription => @"
    AI 对话与文本处理插件
    常用方法：ModelSet(api, key[, model[, optionsJson]])、Chat(message, output)、ChatEx(message, optionsJson, output)、ChatStream(message, optionsJson, output)、ChatJson(json, output)、HttpGetJson(url, json)、ChatRaw(message, json)、
    LastStatus(output)、LastError(output)、JsonGet、RegexGet、SplitGet、Between、Replace
    详细说明见 Readme.md
        ";

        public override string PluginVersion => "2.2";

        public override string PluginAuthor => "zsz";
    }


    internal static class ChatClient
    {
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        private static string url = "http://127.0.0.1:9000/chat";
        private static string apiKey = "";
        private static string model = "";
        private static readonly JsonObject globalOptions = new();
        private static string lastError = "";
        private static string lastJson = "";
        private static long lastStatus;

        public static string LastError => lastError;
        public static string LastJson => lastJson;
        public static long LastStatus => lastStatus;

        public static void Configure(string newUrl, string newApiKey, string newModel)
        {
            Configure(newUrl, newApiKey, newModel, "");
        }

        public static void Configure(string newUrl, string newApiKey, string newModel, string newOptions)
        {
            url = string.IsNullOrWhiteSpace(newUrl) ? "http://127.0.0.1:9000/chat" : newUrl.Trim();
            apiKey = newApiKey?.Trim() ?? "";
            model = newModel?.Trim() ?? "";
            JsonObject parsed = ParseOptions(newOptions, out string error);
            if (error != "")
            {
                SetResult(2, $"ModelSet: {error}");
                return;
            }
            globalOptions.Clear();
            foreach (var property in parsed)
                globalOptions[property.Key] = property.Value?.DeepClone();
            SetResult(0, "");
        }

        public static string Send(string message)
        {
            string rawJson;
            return Send(message, out rawJson);
        }

        public static string Send(string message, out string rawJson)
        {
            return Send(message, "", out rawJson, null, false);
        }

        
        public static string Send(string message, string callOptions, out string rawJson)
        {
            return Send(message, callOptions, out rawJson, null, false);
        }
        
        // onDelta 非 null 时回调   
        // forceStream 会强制给 OpenAI 端点加上 "stream":true   
        public static string Send(string message, string callOptions, out string rawJson, Action<string>? onDelta, bool forceStream)
        {
            JsonObject payload = MergeOptions(globalOptions, callOptions, out string error);
            if (error != "")
            {
                rawJson = "";
                lastJson = "";
                SetResult(2, $"参数错误：{error}");
                return "";
            }

            // show_reasoning 是插件私有开关：是否把思维链也实时打印出来
            // 不能发给服务端
            bool showReasoning = false;
            if (payload.TryGetPropertyValue("show_reasoning", out JsonNode? showNode) && showNode is not null)
            {
                try { showReasoning = showNode.GetValue<bool>(); }
                catch { showReasoning = false; }
                payload.Remove("show_reasoning");
            }

            if (OpenAiMode)
            {
                if (!payload.ContainsKey("model") && !string.IsNullOrWhiteSpace(model))
                    payload["model"] = model;
                payload["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "user", ["content"] = message ?? "" }
                };
                if (forceStream)
                    payload["stream"] = true;
            }
            else
            {
                payload["message"] = message ?? "";
            }

            return SendJson(payload.ToJsonString(), out rawJson, onDelta, showReasoning);
        }

        /// <summary>流式对话：边收边把增量打印到游戏界面，返回完整回复</summary>
        public static string SendStream(string message, string callOptions, out string rawJson)
        {
            return Send(message, callOptions, out rawJson, PrintDelta, true);
        }

        private static JsonObject ParseOptions(string optionsJson, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(optionsJson))
                return new JsonObject();

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(optionsJson);
            }
            catch (Exception ex)
            {
                error = $"选项必须是合法 JSON：{ex.Message}";
                return new JsonObject();
            }

            if (node is not JsonObject obj)
            {
                error = "选项必须是 JSON 对象，例如 {\"temperature\":0.8}";
                return new JsonObject();
            }
            return obj;
        }

        private static JsonObject MergeOptions(JsonObject baseOptions, string callOptions, out string error)
        {
            var result = new JsonObject();
            foreach (var property in baseOptions)
                result[property.Key] = property.Value?.DeepClone();

            JsonObject overrides = ParseOptions(callOptions, out error);
            if (error != "")
                return result;

            foreach (var property in overrides)
                result[property.Key] = property.Value?.DeepClone();
            return result;
        }

        public static string SendJson(string body, out string rawJson)
        {
            return SendJson(body, out rawJson, null, false);
        }

        public static string SendJson(string body, out string rawJson, Action<string>? onDelta)
        {
            return SendJson(body, out rawJson, onDelta, false);
        }

        public static string SendJson(string body, out string rawJson, Action<string>? onDelta, bool showReasoning)
        {
            rawJson = "";
            lastJson = "";
            try
            {
                using JsonDocument validation = JsonDocument.Parse(body ?? "");
                if (IsStreamRequested(validation.RootElement))
                    return SendStreaming(body, onDelta, out rawJson, showReasoning);

                using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
                ApplyAuth(request);
                request.Content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
                using HttpResponseMessage response = PumpUntil(client.SendAsync(request));
                string json = PumpUntil(response.Content.ReadAsStringAsync());
                rawJson = json;
                lastJson = json;
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {json}");

                using JsonDocument document = JsonDocument.Parse(json);
                string? result = ExtractReply(document.RootElement);
                if (result == null)
                    throw new InvalidOperationException("响应中没有找到 content、response 或 choices[0] 内容");

                SetResult(0, "");
                return result;
            }
            catch (Exception ex)
            {
                SetResult(1, ex.Message);
                return "";
            }
        }

        public static string GetJson(string endpoint)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                ApplyAuth(request);
                using HttpResponseMessage response = PumpUntil(client.SendAsync(request));
                string json = PumpUntil(response.Content.ReadAsStringAsync());
                lastJson = json;
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {json}");
                using JsonDocument document = JsonDocument.Parse(json);
                SetResult(0, "");
                return json;
            }
            catch (Exception ex)
            {
                lastJson = "";
                SetResult(1, ex.Message);
                return "";
            }
        }

        private const int PumpIntervalMs = 50;

        private static void ApplyAuth(HttpRequestMessage request)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        // 等待后台请求完成的同时，用 ERB 的 WAIT 指令同款原语 Await 重置引擎的无限循环计时器并泵消息
        private static T PumpUntil<T>(Task<T> task)
        {
            if (!task.IsCompleted)
            {
                PluginManager manager = PluginManager.GetInstance();
                while (!task.IsCompleted)
                {
                    try { manager.Await(PumpIntervalMs); }
                    catch { System.Threading.Thread.Sleep(PumpIntervalMs); }
                }
            }
            return task.GetAwaiter().GetResult();
        }

        private static bool IsStreamRequested(JsonElement root)
        {
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("stream", out JsonElement stream)
                && stream.ValueKind == JsonValueKind.True;
        }

        private sealed class StreamState
        {
            public readonly object Sync = new();
            public readonly StringBuilder Text = new();
            public readonly StringBuilder Thinking = new();
            public readonly StringBuilder AllText = new();
            public int Emitted;
            public int ThinkingEmitted;
            public int DataLines;
            public int ContentChunks;
            public string FirstDataLine = "";
            public bool SawSse;
            public bool Done;
            public string Error = "";
        }

        // OpenAI 流式：读 SSE 的 data: 行，收集 choices[0].delta.content；
        // 推理模型（DeepSeek 等）先流 reasoning_content 再流 content，两者都收集
        // 服务端若忽略 stream 直接返回普通 JSON，会在结束时回退到普通解析
        private static string SendStreaming(string body, Action<string>? onDelta, out string rawJson, bool showReasoning)
        {
            rawJson = "";
            lastJson = "";
            var state = new StreamState();

            _ = Task.Run(() =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
                    ApplyAuth(request);
                    request.Content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
                    using HttpResponseMessage response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                    {
                        string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        lock (state.Sync)
                        {
                            state.Error = $"HTTP {(int)response.StatusCode}: {err}";
                            state.Done = true;
                        }
                        return;
                    }

                    using var stream = response.Content.ReadAsStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!line.StartsWith("data:", StringComparison.Ordinal))
                        {
                            lock (state.Sync) state.AllText.Append(line).Append('\n');
                            continue;
                        }

                        string data = line.Substring(5).Trim();
                        lock (state.Sync)
                        {
                            state.SawSse = true;
                            state.DataLines++;
                            if (state.FirstDataLine.Length == 0 && data.Length > 0)
                                state.FirstDataLine = data.Length > 160 ? data.Substring(0, 160) : data;
                        }
                        if (data.Length == 0 || data == "[DONE]")
                            continue;

                        ExtractDeltas(data, out string content, out string reasoning);
                        lock (state.Sync)
                        {
                            if (content.Length > 0) { state.Text.Append(content); state.ContentChunks++; }
                            if (reasoning.Length > 0) state.Thinking.Append(reasoning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (state.Sync) state.Error = ex.Message;
                }
                finally
                {
                    lock (state.Sync) state.Done = true;
                }
            });

            PluginManager manager = PluginManager.GetInstance();
            while (true)
            {
                bool done;
                lock (state.Sync) done = state.Done;
                if (done) break;
                if (onDelta != null) EmitPending(state, onDelta, showReasoning);
                try { manager.Await(PumpIntervalMs); }
                catch { System.Threading.Thread.Sleep(PumpIntervalMs); }
            }
            if (onDelta != null) EmitPending(state, onDelta, showReasoning);

            string error;
            bool sawSse;
            string streamed;
            string thinking;
            string allText;
            int dataLines;
            int contentChunks;
            string firstDataLine;
            lock (state.Sync)
            {
                error = state.Error;
                sawSse = state.SawSse;
                streamed = state.Text.ToString();
                thinking = state.Thinking.ToString();
                allText = state.AllText.ToString();
                dataLines = state.DataLines;
                contentChunks = state.ContentChunks;
                firstDataLine = state.FirstDataLine;
            }

            if (error != "")
            {
                SetResult(1, error);
                return "";
            }

            if (sawSse)
            {
                // 推理模型可能把预算全用在 reasoning 上，content 为空时退回思维链文本
                string reply = streamed.Length > 0 ? streamed : thinking;
                if (reply.Length == 0)
                {
                    SetResult(1, $"流式响应中没有找到内容（data行={dataLines} content块={contentChunks} 首行={firstDataLine}）");
                    return "";
                }
                rawJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = reply } } } });
                lastJson = rawJson;
                SetResult(0, "");
                return reply;
            }

            // 服务端不支持流式，返回的是普通 JSON
            rawJson = allText.Trim();
            lastJson = rawJson;
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawJson);
                string? result = ExtractReply(document.RootElement);
                if (result == null)
                    throw new InvalidOperationException("响应中没有找到 content、response 或 choices[0] 内容");
                SetResult(0, "");
                return result;
            }
            catch (Exception ex)
            {
                SetResult(1, ex.Message);
                return "";
            }
        }

        private static void EmitPending(StreamState state, Action<string> onDelta, bool showReasoning)
        {
            if (showReasoning)
            {
                string think;
                lock (state.Sync)
                {
                    if (state.Thinking.Length <= state.ThinkingEmitted)
                        think = "";
                    else
                    {
                        think = state.Thinking.ToString(state.ThinkingEmitted, state.Thinking.Length - state.ThinkingEmitted);
                        state.ThinkingEmitted = state.Thinking.Length;
                    }
                }
                if (think.Length > 0) onDelta(think);
            }

            string chunk;
            lock (state.Sync)
            {
                if (state.Text.Length <= state.Emitted) return;
                chunk = state.Text.ToString(state.Emitted, state.Text.Length - state.Emitted);
                state.Emitted = state.Text.Length;
            }
            onDelta(chunk);
        }

        private static void ExtractDeltas(string json, out string content, out string reasoning)
        {
            content = "";
            reasoning = "";
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("choices", out JsonElement choices)
                    && choices.ValueKind == JsonValueKind.Array
                    && choices.GetArrayLength() > 0)
                {
                    JsonElement choice = choices[0];
                    if (choice.TryGetProperty("delta", out JsonElement delta) && delta.ValueKind == JsonValueKind.Object)
                    {
                        if (delta.TryGetProperty("content", out JsonElement deltaContent) && deltaContent.ValueKind == JsonValueKind.String)
                            content = deltaContent.GetString() ?? "";
                        if (delta.TryGetProperty("reasoning_content", out JsonElement deltaReasoning) && deltaReasoning.ValueKind == JsonValueKind.String)
                            reasoning = deltaReasoning.GetString() ?? "";
                    }
                    if (content.Length == 0 && choice.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.Object)
                    {
                        if (message.TryGetProperty("content", out JsonElement messageContent) && messageContent.ValueKind == JsonValueKind.String)
                            content = messageContent.GetString() ?? "";
                        if (content.Length == 0 && message.TryGetProperty("reasoning_content", out JsonElement messageReasoning) && messageReasoning.ValueKind == JsonValueKind.String)
                            content = messageReasoning.GetString() ?? "";
                    }
                    if (content.Length == 0 && choice.TryGetProperty("text", out JsonElement text) && text.ValueKind == JsonValueKind.String)
                        content = text.GetString() ?? "";
                }
                if (content.Length == 0 && reasoning.Length == 0)
                {
                    if (root.TryGetProperty("response", out JsonElement response) && response.ValueKind == JsonValueKind.String)
                        content = response.GetString() ?? "";
                    else if (root.TryGetProperty("content", out JsonElement rootContent) && rootContent.ValueKind == JsonValueKind.String)
                        content = rootContent.GetString() ?? "";
                }
            }
            catch
            {
                // 非 JSON 的 data 行直接忽略
            }
        }

        private static void PrintDelta(string chunk)
        {
            try
            {
                PluginManager manager = PluginManager.GetInstance();
                string[] parts = chunk.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) manager.PrintNewLine();
                    if (parts[i].Length > 0) manager.PrintPlain(parts[i]);
                }
                manager.FlushConsole(true);
            }
            catch
            {
                // 界面不可用时忽略实时显示，回复文本仍会正常返回
            }
        }

        public static void SetResult(long status, string error)
        {
            lastStatus = status;
            lastError = error ?? "";
        }

        private static bool IsOpenAiEndpoint(string endpoint)
        {
            return endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);
        }

        // 允许只填 OpenAI 的 base_url（例如 https://api.deepseek.com/v1），自动补上 /chat/completions
        private static string ResolveEndpoint(string endpoint)
        {
            if (IsOpenAiEndpoint(endpoint))
                return endpoint;
            string trimmed = (endpoint ?? "").TrimEnd('/');
            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return trimmed + "/chat/completions";
            return endpoint;
        }

        private static bool OpenAiMode => IsOpenAiEndpoint(RequestUrl);
        private static string RequestUrl => ResolveEndpoint(url);

        private static string? ExtractReply(JsonElement root)
        {
            if (root.TryGetProperty("content", out JsonElement content))
                return content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : content.ToString();

            if (root.TryGetProperty("response", out JsonElement response))
                return response.ValueKind == JsonValueKind.String ? response.GetString() ?? "" : response.ToString();

            if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                JsonElement choice = choices[0];
                if (choice.TryGetProperty("message", out JsonElement message))
                {
                    if (message.TryGetProperty("content", out JsonElement messageContent) && messageContent.ValueKind != JsonValueKind.Null)
                    {
                        string text = messageContent.ValueKind == JsonValueKind.String ? messageContent.GetString() ?? "" : messageContent.ToString();
                        if (text.Length > 0) return text;
                    }
                    // 推理模型：content 为空时退回 reasoning_content
                    if (message.TryGetProperty("reasoning_content", out JsonElement reasoning) && reasoning.ValueKind == JsonValueKind.String)
                    {
                        string text = reasoning.GetString() ?? "";
                        if (text.Length > 0) return text;
                    }
                }
                if (choice.TryGetProperty("text", out JsonElement choiceText))
                    return choiceText.GetString() ?? choiceText.ToString();
            }

            return null;
        }
    }

    public class ModelSetMethod : IPluginMethod
    {
        public string Name => "ModelSet";
        public string Description => "设置 AI 地址、API Key、可选模型和可选参数：ModelSet(api, key[, model[, optionsJson]])";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2)
            {
                ChatClient.SetResult(2, "ModelSet 至少需要 api 和 key");
                return;
            }
            ChatClient.Configure(
                args[0].strValue,
                args[1].strValue,
                args.Length >= 3 ? args[2].strValue : "",
                args.Length >= 4 ? args[3].strValue : "");
        }
    }

    public class ChatMethod : IPluginMethod
    {
        public string Name => "Chat";
        public string Description => "发送消息并返回回复：Chat(message, output)；兼容旧版 7 参数格式";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2) return;

            string reply = ChatClient.Send(args[0].strValue ?? "");
            args[1].strValue = ChatClient.LastStatus == 0 ? reply : "";
            if (args.Length >= 7)
                SetLegacyFields(ChatClient.LastJson, args);
        }

        private static void SetLegacyFields(string json, PluginMethodParameter[] args)
        {
            args[2].strValue = JsonField(json, "favor");
            args[3].strValue = JsonField(json, "trust");
            args[4].strValue = JsonField(json, "command_exists");
            args[5].strValue = JsonField(json, "command_type");
            args[6].strValue = JsonField(json, "disgust");
        }

        private static string JsonField(string json, string name)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty(name, out JsonElement value)) return "";
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
            }
            catch
            {
                return "";
            }
        }
    }

    public class ChatExMethod : IPluginMethod
    {
        public string Name => "ChatEx";
        public string Description => "按 OpenAI 格式发送消息并可传入参数字段：ChatEx(message, optionsJson, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            string reply = ChatClient.Send(args[0].strValue ?? "", args[1].strValue ?? "", out _);
            args[2].strValue = ChatClient.LastStatus == 0 ? reply : "";
        }
    }

    public class ChatStreamMethod : IPluginMethod
    {
        public string Name => "ChatStream";
        public string Description => "流式发送消息并边收边显示：ChatStream(message, optionsJson, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            string reply = ChatClient.SendStream(args[0].strValue ?? "", args[1].strValue ?? "", out _);
            args[2].strValue = ChatClient.LastStatus == 0 ? reply : "";
        }
    }

    public class ChatJsonMethod : IPluginMethod
    {
        public string Name => "ChatJson";
        public string Description => "发送自定义 JSON 请求并返回回复：ChatJson(json, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2) return;
            string reply = ChatClient.SendJson(args[0].strValue ?? "", out _);
            args[1].strValue = ChatClient.LastStatus == 0 ? reply : "";
        }
    }

    public class HttpGetJsonMethod : IPluginMethod
    {
        public string Name => "HttpGetJson";
        public string Description => "GET JSON 接口并返回原始 JSON：HttpGetJson(url, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2) return;
            args[1].strValue = ChatClient.GetJson(args[0].strValue ?? "");
        }
    }

    public class ChatRawMethod : IPluginMethod
    {
        public string Name => "ChatRaw";
        public string Description => "发送消息并返回服务器原始 JSON：ChatRaw(message, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2) return;
            ChatClient.Send(args[0].strValue ?? "", out string rawJson);
            args[1].strValue = rawJson;
        }
    }

    public class LastStatusMethod : IPluginMethod
    {
        public string Name => "LastStatus";
        public string Description => "读取上一次操作状态：LastStatus(output)；0 表示成功";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length >= 1) args[0].intValue = ChatClient.LastStatus;
        }
    }

    public class LastErrorMethod : IPluginMethod
    {
        public string Name => "LastError";
        public string Description => "读取上一次错误：LastError(output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length >= 1) args[0].strValue = ChatClient.LastError;
        }
    }

    internal static class JsonTools
    {
        public static bool TryParse(string text, out JsonDocument document, out string error)
        {
            try
            {
                document = JsonDocument.Parse(text ?? "");
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                document = null!;
                error = ex.Message;
                return false;
            }
        }

        public static bool TryResolve(JsonElement root, string path, out JsonElement value)
        {
            value = root;
            if (string.IsNullOrWhiteSpace(path) || path == "$" || path == ".") return true;

            MatchCollection tokens = Regex.Matches(path, @"(?:^|\.)([^.\[\]]+)|\[(\d+)\]");
            int consumed = 0;
            foreach (Match token in tokens)
            {
                if (token.Index != consumed)
                {
                    value = default;
                    return false;
                }
                consumed = token.Index + token.Length;
                if (token.Groups[1].Success)
                {
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(token.Groups[1].Value, out value))
                    {
                        value = default;
                        return false;
                    }
                }
                else
                {
                    if (value.ValueKind != JsonValueKind.Array || !int.TryParse(token.Groups[2].Value, out int index) || index < 0 || index >= value.GetArrayLength())
                    {
                        value = default;
                        return false;
                    }
                    value = value[index];
                }
            }
            return consumed == path.Length;
        }

        public static string ToText(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        public static string ToRawJson(JsonElement value)
        {
            return JsonSerializer.Serialize(value);
        }

        public static string TypeName(JsonValueKind kind)
        {
            return kind switch
            {
                JsonValueKind.Object => "object",
                JsonValueKind.Array => "array",
                JsonValueKind.String => "string",
                JsonValueKind.Number => "number",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                _ => "undefined"
            };
        }

        public static bool TryGet(JsonElement root, string path, out JsonElement value, out string error)
        {
            if (TryResolve(root, path, out value))
            {
                error = "";
                return true;
            }
            error = $"找不到 JSON 路径：{path}";
            return false;
        }
    }

    public class JsonGetMethod : IPluginMethod
    {
        public string Name => "JsonGet";
        public string Description => "读取 JSON 路径并转为文本：JsonGet(json, path, output)，支持 data.items[0].name";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            try
            {
                if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string parseError))
                    throw new InvalidOperationException(parseError);
                using (document)
                {
                    if (!JsonTools.TryGet(document.RootElement, args[1].strValue ?? "", out JsonElement value, out string error))
                        throw new InvalidOperationException(error);
                    args[2].strValue = JsonTools.ToText(value);
                }
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex)
            {
                ChatClient.SetResult(2, $"JsonGet: {ex.Message}");
                args[2].strValue = "";
            }
        }
    }

    public class JsonGetRawMethod : IPluginMethod
    {
        public string Name => "JsonGetRaw";
        public string Description => "读取 JSON 路径并保留为 JSON 片段：JsonGetRaw(json, path, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            try
            {
                if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string parseError))
                    throw new InvalidOperationException(parseError);
                using (document)
                {
                    if (!JsonTools.TryGet(document.RootElement, args[1].strValue ?? "", out JsonElement value, out string error))
                        throw new InvalidOperationException(error);
                    args[2].strValue = JsonTools.ToRawJson(value);
                }
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex) { ChatClient.SetResult(2, $"JsonGetRaw: {ex.Message}"); args[2].strValue = ""; }
        }
    }

    public class JsonGetIntMethod : IPluginMethod
    {
        public string Name => "JsonGetInt";
        public string Description => "读取 JSON 数字：JsonGetInt(json, path, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            try
            {
                if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string parseError))
                    throw new InvalidOperationException(parseError);
                using (document)
                {
                    if (!JsonTools.TryGet(document.RootElement, args[1].strValue ?? "", out JsonElement value, out string error) || !value.TryGetInt64(out long number))
                        throw new InvalidOperationException(error == "" ? "目标值不是整数" : error);
                    args[2].intValue = number;
                }
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex) { ChatClient.SetResult(2, $"JsonGetInt: {ex.Message}"); args[2].intValue = 0; }
        }
    }

    public class JsonGetBoolMethod : IPluginMethod
    {
        public string Name => "JsonGetBool";
        public string Description => "读取 JSON 布尔值为 0 或 1：JsonGetBool(json, path, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            try
            {
                if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string parseError))
                    throw new InvalidOperationException(parseError);
                using (document)
                {
                    if (!JsonTools.TryGet(document.RootElement, args[1].strValue ?? "", out JsonElement value, out string error) || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
                        throw new InvalidOperationException(error == "" ? "目标值不是布尔值" : error);
                    args[2].intValue = value.ValueKind == JsonValueKind.True ? 1 : 0;
                }
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex) { ChatClient.SetResult(2, $"JsonGetBool: {ex.Message}"); args[2].intValue = 0; }
        }
    }

    public class JsonHasMethod : IPluginMethod
    {
        public string Name => "JsonHas";
        public string Description => "判断 JSON 路径是否存在，存在为 1：JsonHas(json, path, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string error))
            {
                ChatClient.SetResult(2, $"JsonHas: {error}"); args[2].intValue = 0; return;
            }
            using (document)
            {
                args[2].intValue = JsonTools.TryResolve(document.RootElement, args[1].strValue ?? "", out _) ? 1 : 0;
            }
            ChatClient.SetResult(0, "");
        }
    }

    public class JsonTypeMethod : IPluginMethod
    {
        public string Name => "JsonType";
        public string Description => "读取 JSON 类型：JsonType(json, path, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            try
            {
                if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string parseError))
                    throw new InvalidOperationException(parseError);
                using (document)
                {
                    if (!JsonTools.TryGet(document.RootElement, args[1].strValue ?? "", out JsonElement value, out string error))
                        throw new InvalidOperationException(error);
                    args[2].strValue = JsonTools.TypeName(value.ValueKind);
                }
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex) { ChatClient.SetResult(2, $"JsonType: {ex.Message}"); args[2].strValue = ""; }
        }
    }

    public class JsonCountMethod : IPluginMethod
    {
        public string Name => "JsonCount";
        public string Description => "读取 JSON 数组长度或对象属性数：JsonCount(json, path, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 3) return;
            try
            {
                if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string parseError))
                    throw new InvalidOperationException(parseError);
                using (document)
                {
                    if (!JsonTools.TryGet(document.RootElement, args[1].strValue ?? "", out JsonElement value, out string error))
                        throw new InvalidOperationException(error);
                    args[2].intValue = value.ValueKind switch
                    {
                        JsonValueKind.Array => value.GetArrayLength(),
                        JsonValueKind.Object => value.EnumerateObject().Count(),
                        _ => throw new InvalidOperationException("目标值不是数组或对象")
                    };
                }
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex) { ChatClient.SetResult(2, $"JsonCount: {ex.Message}"); args[2].intValue = 0; }
        }
    }

    public class JsonValidMethod : IPluginMethod
    {
        public string Name => "JsonValid";
        public string Description => "判断字符串是否为合法 JSON，合法为 1：JsonValid(json, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2) return;
            if (JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out _))
            {
                document.Dispose(); args[1].intValue = 1; ChatClient.SetResult(0, "");
            }
            else
            {
                args[1].intValue = 0; ChatClient.SetResult(2, "输入不是合法 JSON");
            }
        }
    }

    public class JsonCompactMethod : IPluginMethod
    {
        public string Name => "JsonCompact";
        public string Description => "将 JSON 压缩为单行：JsonCompact(json, output)";

        public void Execute(PluginMethodParameter[] args) => JsonFormatMethods.Format(args, new JsonSerializerOptions { WriteIndented = false }, "JsonCompact");
    }

    public class JsonPrettyMethod : IPluginMethod
    {
        public string Name => "JsonPretty";
        public string Description => "将 JSON 格式化为易读文本：JsonPretty(json, output)";

        public void Execute(PluginMethodParameter[] args) => JsonFormatMethods.Format(args, new JsonSerializerOptions { WriteIndented = true }, "JsonPretty");
    }

    internal static class JsonFormatMethods
    {
        public static void Format(PluginMethodParameter[] args, JsonSerializerOptions options, string methodName)
        {
            if (args.Length < 2) return;
            try
            {
                if (!JsonTools.TryParse(args[0].strValue ?? "", out JsonDocument document, out string parseError))
                    throw new InvalidOperationException(parseError);
                using (document) args[1].strValue = JsonSerializer.Serialize(document.RootElement, options);
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex) { ChatClient.SetResult(2, $"{methodName}: {ex.Message}"); args[1].strValue = ""; }
        }
    }

    public class JsonEncodeMethod : IPluginMethod
    {
        public string Name => "JsonEncode";
        public string Description => "将普通文本编码为 JSON 字符串：JsonEncode(text, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2) return;
            args[1].strValue = JsonSerializer.Serialize(args[0].strValue ?? "");
            ChatClient.SetResult(0, "");
        }
    }

    public class JsonDecodeMethod : IPluginMethod
    {
        public string Name => "JsonDecode";
        public string Description => "解码 JSON 字符串字面量：JsonDecode(jsonString, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2) return;
            try
            {
                args[1].strValue = JsonSerializer.Deserialize<string>(args[0].strValue ?? "") ?? "";
                ChatClient.SetResult(0, "");
            }
            catch (Exception ex) { ChatClient.SetResult(2, $"JsonDecode: {ex.Message}"); args[1].strValue = ""; }
        }
    }

    public class RegexGetMethod : IPluginMethod
    {
        public string Name => "RegexGet";
        public string Description => "提取正则捕获组：RegexGet(text, pattern, group, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 4) return;
            try
            {
                Match match = Regex.Match(args[0].strValue ?? "", args[1].strValue ?? "");
                int group = (int)args[2].intValue;
                args[3].strValue = match.Success && group >= 0 && group < match.Groups.Count ? match.Groups[group].Value : "";
            }
            catch (Exception ex)
            {
                ChatClient.SetResult(2, $"RegexGet: {ex.Message}");
                args[3].strValue = "";
            }
        }
    }

    public class SplitGetMethod : IPluginMethod
    {
        public string Name => "SplitGet";
        public string Description => "按分隔符取得一段：SplitGet(text, delimiter, index, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 4) return;
            string[] parts = (args[0].strValue ?? "").Split(args[1].strValue ?? "", StringSplitOptions.None);
            int index = (int)args[2].intValue;
            args[3].strValue = index >= 0 && index < parts.Length ? parts[index] : "";
        }
    }

    public class BetweenMethod : IPluginMethod
    {
        public string Name => "Between";
        public string Description => "提取两个标记之间的文本：Between(text, start, end, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 4) return;
            string text = args[0].strValue ?? "";
            string startText = args[1].strValue ?? "";
            int start = text.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0) { args[3].strValue = ""; return; }
            start += startText.Length;
            int end = text.IndexOf(args[2].strValue ?? "", start, StringComparison.Ordinal);
            args[3].strValue = end < 0 ? "" : text.Substring(start, end - start);
        }
    }

    public class ReplaceMethod : IPluginMethod
    {
        public string Name => "Replace";
        public string Description => "替换文本：Replace(text, old, new, output)";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 4) return;
            args[3].strValue = (args[0].strValue ?? "").Replace(args[1].strValue ?? "", args[2].strValue ?? "", StringComparison.Ordinal);
        }

    }




    public class TestBuiltinFunctions : IPluginMethod
    {
        public string Name => "TestAPI";

        public string Description => "Tests basic API";

        public void Execute(PluginMethodParameter[] args)
        {
            var api = PluginManager.GetInstance();
            api.ClearDisplay();
            api.SetBgColor(System.Drawing.Color.Green);
            api.Print("Press any key");
            api.PrintNewLine();
            api.ReadAnyKey();
            long day = api.GetIntVar("DAY");
            long money = api.GetIntVar("MONEY");
            api.Print($"Day: {day} Money {money}\n");
            api.SetIntVar("DAY", 31);
            api.SetIntVar("MONEY", 100500);
            api.Print($"New Day: {api.GetIntVar("DAY")} New Money {api.GetIntVar("MONEY")}\n");
            api.Print("Testing Finished. Press any key\n");
            api.ReadAnyKey();
            api.Quit();
        }
    }
}
