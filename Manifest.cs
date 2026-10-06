using MinorShift.Emuera.Runtime.Utils.PluginSystem;
using System;
using System.Windows;
using System.Windows.Forms;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
    AI 对话与文本处理插件。
    常用方法：ModelSet(api, key[, model])、Chat(message, output)、ChatJson(json, output)、HttpGetJson(url, json)、ChatRaw(message, json)、
    LastStatus(output)、LastError(output)、JsonGet、RegexGet、SplitGet、Between、Replace。
    详细说明见 Readme/ChatCallSharp_API.md。
        ";

        public override string PluginVersion => "2.0";

        public override string PluginAuthor => "zsz";
    }


    internal static class ChatClient
    {
        private static readonly HttpClient client = new HttpClient();
        private static string url = "http://127.0.0.1:9000/chat";
        private static string apiKey = "";
        private static string model = "";
        private static string lastError = "";
        private static string lastJson = "";
        private static long lastStatus;

        public static string LastError => lastError;
        public static string LastJson => lastJson;
        public static long LastStatus => lastStatus;

        public static void Configure(string newUrl, string newApiKey, string newModel)
        {
            url = string.IsNullOrWhiteSpace(newUrl) ? "http://127.0.0.1:9000/chat" : newUrl.Trim();
            apiKey = newApiKey?.Trim() ?? "";
            model = newModel?.Trim() ?? "";
            SetResult(0, "");
        }

        public static string Send(string message)
        {
            string rawJson;
            return Send(message, out rawJson);
        }

        public static string Send(string message, out string rawJson)
        {
            object payload = IsOpenAiEndpoint(url)
                    ? new { model, messages = new[] { new { role = "user", content = message } } }
                    : new { message };
            return SendJson(JsonSerializer.Serialize(payload), out rawJson);
        }

        public static string SendJson(string body, out string rawJson)
        {
            rawJson = "";
            lastJson = "";
            try
            {
                using JsonDocument validation = JsonDocument.Parse(body ?? "");
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
                using HttpResponseMessage response = client.Send(request);
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
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
                if (!string.IsNullOrWhiteSpace(apiKey))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                using HttpResponseMessage response = client.Send(request);
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
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

        public static void SetResult(long status, string error)
        {
            lastStatus = status;
            lastError = error ?? "";
        }

        private static bool IsOpenAiEndpoint(string endpoint)
        {
            return endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExtractReply(JsonElement root)
        {
            if (root.TryGetProperty("content", out JsonElement content))
                return content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : content.ToString();

            if (root.TryGetProperty("response", out JsonElement response))
                return response.ValueKind == JsonValueKind.String ? response.GetString() ?? "" : response.ToString();

            if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                JsonElement choice = choices[0];
                if (choice.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement messageContent))
                    return messageContent.GetString() ?? messageContent.ToString();
                if (choice.TryGetProperty("text", out JsonElement text))
                    return text.GetString() ?? text.ToString();
            }

            return null;
        }
    }

    public class ModelSetMethod : IPluginMethod
    {
        public string Name => "ModelSet";
        public string Description => "设置 AI 地址、API Key 和可选模型：ModelSet(api, key[, model])";

        public void Execute(PluginMethodParameter[] args)
        {
            if (args.Length < 2)
            {
                ChatClient.SetResult(2, "ModelSet 至少需要 api 和 key");
                return;
            }
            ChatClient.Configure(args[0].strValue, args[1].strValue, args.Length >= 3 ? args[2].strValue : "");
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
