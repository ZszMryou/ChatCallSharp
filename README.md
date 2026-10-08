# ChatCallSharp 函数使用方案

ChatCallSharp 是给 Emuera ERB 使用的同步插件。所有函数通过 `CALLSHARP` 调用，输出变量必须作为最后一个参数传入。


将bin/Debug/net8.0-windows/ChatCallSharp.dll放入".\你的era的游戏目录\plugins"即可使用

本插件更多面向于想在口上或其它玩法功能上接入ai的创作者

本插件附带readme（就是这个文件，，，） vibe coding时可以将本文件发送给ai

> 本版新增：`ModelSet` 第 4 个参数、`ChatEx`、`ChatStream`（流式）及其 `show_reasoning` 开关。**可直接套用的完整示例脚本见第 10 节。**

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
CALLSHARP ModelSet(api, key, model, optionsJson)
```

设置服务器地址、API Key 和模型。设置只保存在当前 Emuera 进程内，重启游戏后需要重新设置。

`optionsJson` 是可选的第 4 个参数：一个 JSON 对象，里面的字段会合并进请求体。用来传 OpenAI 风格的模型参数（`temperature`、`top_p`、`max_tokens`、`presence_penalty`、`frequency_penalty` 等），也可以在这里覆盖 `model`。

```erb
CALLSHARP ModelSet("http://127.0.0.1:9000/chat", "")
; OpenAI 兼容接口
CALLSHARP ModelSet("https://example.com/v1/chat/completions", "sk-example", "gpt-4o-mini")
; 带参数：温度 0.8，最多生成 1024 token
CALLSHARP ModelSet("https://example.com/v1/chat/completions", "sk-example", "gpt-4o-mini", "{\"temperature\":0.8,\"max_tokens\":1024}")
```

地址包含 `/chat/completions`（或以 `/v1` 结尾）时发送：

```json
{"model":"模型名","temperature":0.8,"messages":[{"role":"user","content":"消息"}]}
```

其他地址发送：

```json
{"message":"消息","temperature":0.8}
```

非空 Key 会以 `Authorization: Bearer <你的Key>` 请求头发送。插件不会保存 Key。

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

### ChatEx

```erb
CALLSHARP ChatEx(message, optionsJson, response)
```

和 `Chat` 一样发送一条消息，但多一个 `optionsJson` 参数，用来传**这一次调用**的参数字段。它会先合并 `ModelSet` 的全局参数，再合并进请求体，所以可以临时覆盖温度、模型等设置。

```erb
; 这一次用更低的温度、并换一个模型
CALLSHARP ChatEx("用一句话夸夸对面。", "{\"temperature\":0.2,\"model\":\"gpt-4o\"}", RESPONSE)
```

### ChatStream

```erb
CALLSHARP ChatStream(message, optionsJson, response)
```

**流式**对话：请求会带上 `"stream":true`，插件一边接收服务器的 SSE 增量、一边实时打印到游戏界面，函数返回时把完整回复写入 `response`。想要「打字机」效果时用它。

```erb
; 文字会在生成过程中逐段出现；返回后 RESPONSE 里是完整回复
CALLSHARP ChatStream("用 300 字介绍一下东方Project。", "{\"temperature\":0.8,\"max_tokens\":4096}", RESPONSE)
```

- 只对 OpenAI 端点（地址含 `/chat/completions` 或以 `/v1` 结尾）生效；其他地址等同于 `Chat`。
- 服务端若不支持流式、直接返回普通 JSON，插件会自动回退成普通解析，照样能拿到回复。
- 推理模型（DeepSeek 等）会先流思维链 `delta.reasoning_content`、再流正文 `delta.content`。插件两者都收集：`response` 取正文，正文为空时才退回思维链。实时打印默认只打正文，加 `"show_reasoning":true` 可以把思维链也打出来。该开关是插件私有参数，发送前会从请求体里移除，不会传给服务端。

```erb
; 思维链和正文都逐段打印
CALLSHARP ChatStream("用 300 字介绍一下东方Project。", "{\"temperature\":0.8,\"show_reasoning\":true}", RESPONSE)
```

### 思考模式（thinking / reasoning_effort）

「是否思考」和「思考强度」是**服务端参数**，直接写进 `optionsJson` 即可（插件会把字段原样合并进请求体）。以 DeepSeek 为例：

```erb
; 开启思考，强度 high（默认档）
"{\"thinking\":{\"type\":\"enabled\"},\"reasoning_effort\":\"high\"}"

; 强度档位：low / high（默认） / max
"{\"thinking\":{\"type\":\"enabled\"},\"reasoning_effort\":\"max\"}"

; 关闭思考。注意：关闭时不要再发 reasoning_effort，否则服务端会报错
"{\"thinking\":{\"type\":\"disabled\"}}"
```

思考过程产生的 token 也计入 `max_tokens`，开着思考时请把 `max_tokens` 设大一些。

**是否显示思考过程**用插件的私有开关 `show_reasoning`（默认 `false`）：

```erb
; 显示思维链
CALLSHARP ChatStream("介绍一下东方Project。", "{\"thinking\":{\"type\":\"enabled\"},\"reasoning_effort\":\"high\",\"show_reasoning\":true}", RESPONSE)
```

- `show_reasoning` 只影响**实时打印**，且只对 `ChatStream` 有效（其他函数本来就不打印）。它发送前会从请求体里移除，不会传给服务端。
- 输出变量 `RESPONSE` 始终优先取正文；只有正文为空时才退回思维链文本。
- **不要**把 `show_reasoning` 放进 `ChatJson`：该函数的请求体是原样转发的，会把未知字段发给服务端。

### 生成思考参数的辅助函数

嫌手拼 `\"` 麻烦的话，用一个函数把「强度 / 是否显示 / 长度」拼成完整的 `optionsJson`：

```erb
; 强度：0=关闭思考，1=low，2=high，3=max
; 显示思考：0=隐藏，1=显示
; 返回可直接传给 ChatEx / ChatStream 的 optionsJson 字符串
@AI思考选项(强度, 显示思考, 最大token)
#FUNCTION
#DIM 强度
#DIM 显示思考
#DIM 最大token
#DIMS DYNAMIC EFFORT
#DIMS DYNAMIC 思考部分
#DIMS DYNAMIC 显示部分

SIF 强度 <= 0
    思考部分 = "\"thinking\":{\"type\":\"disabled\"}"
ELSE
    IF 强度 == 1
        EFFORT = "low"
    ELSEIF 强度 == 2
        EFFORT = "high"
    ELSE
        EFFORT = "max"
    ENDIF
    思考部分 = "\"thinking\":{\"type\":\"enabled\"},\"reasoning_effort\":\"" + EFFORT + "\""
ENDIF

IF 显示思考
    显示部分 = "true"
ELSE
    显示部分 = "false"
ENDIF

RETURNF "{" + 思考部分 + ",\"show_reasoning\":" + 显示部分 + ",\"max_tokens\":" + TOSTR(最大token) + "}"
```

用法：

```erb
#DIMS DYNAMIC OPT
#DIMS DYNAMIC REPLY

; 强度 max + 显示思维链 + 4096 token
OPT '= AI思考选项(3, 1, 4096)
CALLSHARP ChatStream("用 300 字介绍一下东方Project。", OPT, REPLY)

; 关闭思考、隐藏过程
OPT '= AI思考选项(0, 0, 512)
CALLSHARP Chat("今天天气不错。", REPLY)
```

### ChatJson

```erb
CALLSHARP ChatJson(json, output)
```

发送调用者提供的 JSON 请求体，并从响应的 `content`、`response` 或 OpenAI `choices[0]` 中提取文本。它适合调用需要额外字段的角色接口，例如：

```erb
CALLSHARP JsonEncode(RESULTS, RESULTS:2)
CALLSHARP ChatJson("{\"input\":" + RESULTS:2 + ",\"conversation_id\":\"com501\",\"stream\":false}", RESPONSE)
```

`ChatJson` 只负责发送 JSON 字符串，不会替调用者自动添加 `input`、`conversation_id` 等字段；输入必须是合法 JSON。

AirPChat 角色接口的配置示例：

```erb
CALLSHARP ModelSet("http://127.0.0.1:8765/api/characters/3/chat", "")
CALLSHARP ChatJson("{\"input\":\"你好\",\"conversation_id\":\"com501\",\"stream\":false}", RESPONSE)
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

- HTTP 调用在后台线程执行，不会卡住游戏界面；等待期间插件会周期性重置引擎的「无限循环」计时器，长请求不会弹无限循环警告。请求超时 120 秒。
- 流式输出由 `ChatStream` 提供（或在 `optionsJson` 里写 `"stream":true`）；增量由插件内部解析，输出变量仍然只在函数返回时一次性写入完整文本。
- 插件不负责取消请求或 JSON 对象持久化。
- 插件不提供 `JsonSet`、`JsonDelete` 等原地修改函数，因为 ERB 传入的是字符串，不是可变 JSON 引用；这类操作应在服务器端完成，或由脚本重新拼接结果。
- 旧版七参数 `Chat` 调用仍然保留，新的脚本建议使用 `Chat`/`ChatEx`/`ChatStream`/`ChatRaw` 配合 JSON 提取函数。

## 10. 示例脚本

以下都是可直接粘进 ERB 目录的完整函数。`#DIM` 是整数变量，`#DIMS` 是字符串变量。

### 例 1：最小可用

```erb
@AI最小示例
#DIM DYNAMIC ST
#DIMS DYNAMIC REPLY

CALLSHARP ModelSet("https://api.deepseek.com/chat/completions", "sk-你的Key", "deepseek-flash")
CALLSHARP Chat("你好，请用一句话介绍你自己。", REPLY)

CALLSHARP LastStatus(ST)
IF ST != 0
    CALLSHARP LastError(REPLY)
    SETCOLOR 0xFF0000
    PRINTFORML 调用失败：%REPLY%
    RESETCOLOR
    RETURN 0
ENDIF

PRINTFORML AI：%REPLY%
RETURN 1
```

### 例 2：全局参数 + 单次覆盖

```erb
@AI带参数示例
#DIM DYNAMIC ST
#DIMS DYNAMIC REPLY

; 全局默认：温度 0.8，最多 1024 token
CALLSHARP ModelSet("https://api.deepseek.com/chat/completions", "sk-你的Key", "deepseek-flash", "{\"temperature\":0.8,\"max_tokens\":1024}")

; 这一次换成更低的温度、更短的输出
CALLSHARP ChatEx("用一句话夸夸对面。", "{\"temperature\":0.2,\"max_tokens\":128}", REPLY)
CALLSHARP LastStatus(ST)
SIF ST != 0
    RETURN 0

PRINTFORML AI：%REPLY%
RETURN 1
```

### 例 3：流式输出（打字机效果）

```erb
@AI流式示例
#DIM DYNAMIC ST
#DIMS DYNAMIC REPLY

CALLSHARP ModelSet("https://api.deepseek.com/chat/completions", "sk-你的Key", "deepseek-flash", "{\"temperature\":0.8,\"max_tokens\":4096}")

PRINTL AI 正在输入……
; 想看思维链就把 show_reasoning 打开
CALLSHARP ChatStream("用 300 字介绍一下东方Project。", "{\"temperature\":0.8,\"show_reasoning\":true}", REPLY)
CALLSHARP LastStatus(ST)
PRINTL
SIF ST != 0
    RETURN 0

PRINTFORML 完整回复：%REPLY%
RETURN 1
```

### 例 4：不用 JSON —— 标记文本 + 正则提取

```erb
@AI标记文本示例
#DIM DYNAMIC ST
#DIM DYNAMIC FAVOR
#DIMS DYNAMIC REPLY
#DIMS DYNAMIC TEXT
#DIMS DYNAMIC NUM

CALLSHARP ModelSet("https://api.deepseek.com/chat/completions", "sk-你的Key", "deepseek-flash")
CALLSHARP Chat("请只输出一行：[REPLY]你的回复[/REPLY] 好感度：+3", REPLY)

CALLSHARP LastStatus(ST)
SIF ST != 0
    RETURN 0

CALLSHARP Between(REPLY, "[REPLY]", "[/REPLY]", TEXT)
CALLSHARP RegexGet(REPLY, "好感度[：:]([+-]?[0-9]+)", 1, NUM)
FAVOR = TOINT(NUM)

PRINTFORML 回复：%TEXT%
PRINTFORML 好感度：{FAVOR}
RETURN 1
```

### 例 5：做成一个自定义指令按钮

放进 ERB 目录后，会出现在游戏的自定义指令列表里。

```erb
@ADD_CUSTOM_COM502
#DIM DYNAMIC ST
#DIMS DYNAMIC REPLY

CALLSHARP ModelSet("https://api.deepseek.com/chat/completions", "sk-你的Key", "deepseek-flash", "{\"temperature\":0.8,\"max_tokens\":2048}")
CALLSHARP Chat("请以一个幻想乡少女的口吻，对主角说一句欢迎的话。", REPLY)

CALLSHARP LastStatus(ST)
IF ST != 0
    CALLSHARP LastError(REPLY)
    PRINTFORML 请求失败：%REPLY%
    RETURN 0
ENDIF

SETCOLOR 0xFFAAFF
PRINTFORML %CALLNAME:TARGET%：%REPLY%
RESETCOLOR
RETURN 1


@ADD_CUSTOM_COM_ABLE502
RETURN 1


@Custom_COM502_NAME
#FUNCTIONS
RETURNF "AI回应"
```