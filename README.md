# ChatCallSharp 函数使用方案

ChatCallSharp 是给 Emuera ERB 使用的同步插件。所有函数通过 `CALLSHARP` 调用，输出变量必须作为最后一个参数传入。


将bin/Debug/net8.0-windows/ChatCallSharp.dll放入".\你的era的游戏目录\plugins"即可使用

本插件更多面向于想在口上或其它玩法功能上接入ai的创作者

本插件附带readme（就是这个文件，，，） vibe coding时可以将本文件发送给ai

## 1. 基本规则

```erb
CALLSHARP 函数名(输入参数, 输出变量)
```

插件函数没有 C# 风格的直接返回值。它只能读取 ERB 传入的参数，并把结果写回输出参数，因此：

- 字符串结果写入字符串变量，例如 `#DIMS RESPONSE = ""` 或 `RESULTS:1`。
- 整数结果写入整数变量，例如 `LOCALS:0`。
- JSON 在每次调用时都是字符串，函数结束后不会保留一个可继续修改的 JSON 对象。
- 插件不修改原 JSON 字符串；需要修改时，应提取字段后在 ERB 中重新组合，或交给服务器处理。

## 2. AI 对话

### ModelSet

```erb
CALLSHARP ModelSet(api, key)
CALLSHARP ModelSet(api, key, model)
```

设置服务器地址、API Key 和模型。设置只保存在当前 Emuera 进程内，重启游戏后需要重新设置。

```erb
CALLSHARP ModelSet("http://127.0.0.1:9000/chat", "")
; OpenAI 兼容接口
CALLSHARP ModelSet("https://example.com/v1/chat/completions", "sk-example", "gpt-4o-mini")
```

地址包含 `/chat/completions` 时发送：

```json
{"model":"模型名","messages":[{"role":"user","content":"消息"}]}
```

其他地址发送：

```json
{"message":"消息"}
```

非空 Key 会以 `Authorization: Bearer <key>` 请求头发送。插件不会保存 Key。

### Chat

```erb
CALLSHARP Chat(message, response)
```

发送消息并提取回复文本。支持：

```json
{"response":"你好"}
```

以及 OpenAI 格式：

```json
{"choices":[{"message":{"content":"你好"}}]}
```

### ChatJson

```erb
CALLSHARP ChatJson(json, output)
```

发送调用者提供的 JSON 请求体，并从响应的 `content`、`response` 或 OpenAI `choices[0]` 中提取文本。它适合调用需要额外字段的角色接口，例如：

```erb
CALLSHARP JsonEncode(RESULTS, RESULTS:2)
CALLSHARP ChatJson("{""input"":" + RESULTS:2 + ",""conversation_id"":""com501"",""stream"":false}", RESPONSE)
```

`ChatJson` 只负责发送 JSON 字符串，不会替调用者自动添加 `input`、`conversation_id` 等字段；输入必须是合法 JSON。

AirPChat 角色接口的配置示例：

```erb
CALLSHARP ModelSet("http://127.0.0.1:8765/api/characters/3/chat", "")
CALLSHARP ChatJson("{""input"":""你好"",""conversation_id"":""com501"",""stream"":false}", RESPONSE)
```

### ChatRaw

```erb
CALLSHARP ChatRaw(message, raw_json)
```

返回服务器原始 JSON，适用于需要读取自定义字段的脚本。

### HttpGetJson

```erb
CALLSHARP HttpGetJson(url, output)
```

使用 GET 请求读取 JSON 接口并返回原始 JSON。它不提取回复，也不发送请求体，适合读取 AirPChat 的聊天记录列表：

```erb
CALLSHARP HttpGetJson("http://127.0.0.1:8765/api/characters/1/conversations", RECORDS)
CALLSHARP JsonCount(RECORDS, "conversations", COUNT)
CALLSHARP JsonGet(RECORDS, "conversations[0].conversation_id", ID)
```

读取聊天列表后，脚本应让用户输入一个 `conversation_id`，再把它放入下一次 `ChatJson` 请求的 `conversation_id` 字段。插件不会自动弹出选择窗口，也不会把 JSON 列表转换成 Emuera 菜单。

## 3. JSON 路径规则

所有 JSON 读取函数都接受：

```text
json, path, output
```

路径可以读取对象属性和数组下标：

```erb
; RAW_JSON = {"data":{"items":[{"name":"A"},{"name":"B"}]}}
CALLSHARP JsonGet(RAW_JSON, "data.items[1].name", VALUE)
; VALUE = B
```

根对象可以使用空路径或 `$`：

```erb
CALLSHARP JsonGet(RAW_JSON, "$", ROOT_TEXT)
```

当前路径语法只支持对象属性、非负数组下标和点号，例如 `a.b[0].c`。不支持完整 JSONPath 的筛选器、通配符、表达式或递归搜索。

## 4. JSON 读取函数

### JsonGet

```erb
CALLSHARP JsonGet(json, path, output)
```

读取目标并转成 ERB 字符串。字符串取其文本；数字、布尔、数组和对象取其 JSON 文本表示。

### JsonGetRaw

```erb
CALLSHARP JsonGetRaw(json, path, output)
```

读取目标并序列化为独立 JSON 片段。适合把数组或对象继续交给另一个 JSON 函数。

```erb
CALLSHARP JsonGetRaw(RAW_JSON, "choices[0].message", MESSAGE_JSON)
CALLSHARP JsonGet(MESSAGE_JSON, "content", CONTENT)
```

### JsonGetInt

```erb
CALLSHARP JsonGetInt(json, path, output_int)
```

读取 JSON 整数并直接写入整数变量。目标不是整数时输出 `0`，并设置错误状态。

```erb
CALLSHARP JsonGetInt(RAW_JSON, "favor", FAVOR)
```

### JsonGetBool

```erb
CALLSHARP JsonGetBool(json, path, output_int)
```

读取 JSON 的 `true` 或 `false`，分别写入 `1` 或 `0`。目标不是布尔值时输出 `0`，并设置错误状态。

### JsonHas

```erb
CALLSHARP JsonHas(json, path, output_int)
```

判断路径是否存在。存在写入 `1`，不存在写入 `0`。

### JsonType

```erb
CALLSHARP JsonType(json, path, output)
```

返回以下类型名称之一：

```text
object, array, string, number, true, false, null
```

### JsonCount

```erb
CALLSHARP JsonCount(json, path, output_int)
```

读取数组长度或对象属性数量。目标不是数组或对象时输出 `0` 并设置错误状态。

## 5. JSON 格式处理

### JsonValid

```erb
CALLSHARP JsonValid(text, output_int)
```

判断文本是否为合法 JSON。合法写入 `1`，非法写入 `0`。

### JsonCompact

```erb
CALLSHARP JsonCompact(json, output)
```

解析 JSON 并压缩为单行 JSON。非法输入输出空字符串并设置错误状态。

### JsonPretty

```erb
CALLSHARP JsonPretty(json, output)
```

解析 JSON 并格式化为带缩进的 JSON 文本。它只改变显示格式，不改变数据。

### JsonEncode

```erb
CALLSHARP JsonEncode(text, output)
```

把普通文本编码为 JSON 字符串字面量，包括引号和必要的转义：

```text
输入：他说 "你好"
输出："他说 \"你好\""
```

### JsonDecode

```erb
CALLSHARP JsonDecode(json_string, output)
```

只解码 JSON 字符串字面量。例如输入 `"你好"`，输出 `你好`。它不是通用的 JSON 对象解析函数；对象和数组应使用 `JsonGet` 或 `JsonGetRaw`。

## 6. 错误处理

读取 JSON 成功后，状态会被设为 `0`。解析失败、路径不存在、类型不匹配等情况会清空对应输出，并设置状态：

```erb
CALLSHARP ChatRaw(RESULTS, RAW_JSON)
CALLSHARP JsonGetInt(RAW_JSON, "favor", FAVOR)
CALLSHARP LastStatus(STATUS)

IF STATUS != 0
    CALLSHARP LastError(ERROR)
    PRINTFORML JSON处理失败：%ERROR%
ENDIF
```

```text
0 = 成功
1 = 网络请求或 HTTP 错误
2 = 参数、JSON、路径或类型错误
```

`JsonHas` 在合法 JSON 中遇到不存在路径时返回 `0`，这属于正常判断，不是错误；但 JSON 本身非法时会设置错误状态。

## 7. 推荐的 AI 响应处理

服务器返回：

```json
{
  "response": "你好",
  "favor": 3,
  "meta": {"command": "tea"},
  "items": [{"id": 10}, {"id": 20}]
}
```

ERB：

```erb
CALLSHARP ChatRaw(RESULTS, RAW_JSON)
CALLSHARP JsonGet(RAW_JSON, "response", RESPONSE)
CALLSHARP JsonGetInt(RAW_JSON, "favor", FAVOR)
CALLSHARP JsonGet(RAW_JSON, "meta.command", COMMAND)
CALLSHARP JsonGetInt(RAW_JSON, "items[0].id", FIRST_ID)

CALLSHARP JsonType(RAW_JSON, "items", ITEMS_TYPE)
CALLSHARP JsonCount(RAW_JSON, "items", ITEMS_COUNT)
```

## 8. 文本提取函数

JSON 不能表示服务器返回的非 JSON 普通文本。对于普通文本，请使用这些函数：

```erb
CALLSHARP RegexGet(text, pattern, group, output)
CALLSHARP Between(text, start, end, output)
CALLSHARP SplitGet(text, delimiter, index, output)
CALLSHARP Replace(text, old, new, output)
```

它们不会把普通文本自动转换为 JSON；应先确认输入格式，再选择 JSON 函数或文本函数。

## 9. 兼容和限制

- HTTP 调用是同步的，ERB 会等待服务器返回；网络慢时游戏线程会等待。
- 插件不负责流式输出、后台任务、取消请求或 JSON 对象持久化。
- 插件不提供 `JsonSet`、`JsonDelete` 等原地修改函数，因为 ERB 传入的是字符串，不是可变 JSON 引用；这类操作应在服务器端完成，或由脚本重新拼接结果。
- 旧版七参数 `Chat` 调用仍然保留，新的脚本建议使用 `Chat`/`ChatRaw` 配合 JSON 提取函数。
