using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindBoard.Board.Items;

namespace WindBoard.Board.Persistence
{
    /// <summary>
    /// 笔迹条目的读模式：由版本契约决定，通过 <see cref="JsonSerializerOptions"/> 实例注入转换器。
    /// </summary>
    internal enum InkItemReadMode
    {
        /// <summary>
        /// v1/v2：条目是扁平形态（points/colorRgba 等字段直接在条目对象上）。
        /// 读到包装形态同样接受（不告警），保证旧文件读取行为不变。
        /// </summary>
        LegacyFlat,

        /// <summary>
        /// v3：条目是 <c>{ kind, stroke|shape }</c> 包装形态。
        /// 读到扁平条目时按 <c>kind=stroke</c> 兜底读取，并通过回调上报（每个文件最多告警一次）。
        /// </summary>
        Wrapped,
    }

    /// <summary>
    /// <see cref="InkItemSnapshot"/> 的 JSON 转换器（序列化兼容单点）。
    /// </summary>
    /// <remarks>
    /// - v3 写侧固定输出 <c>{ kind, stroke }</c> 或 <c>{ kind, shape }</c> 包装形态，与 Wbix 元素的 <c>{type, data}</c> 模式一致。
    /// - 读侧兼容 v1/v2 扁平形态（points/colorRgba 等字段直接在条目对象上），读入时包装为 Kind="stroke" 的 <see cref="InkItemSnapshot"/>。
    /// - 读模式由 <see cref="InkItemReadMode"/> 决定，两种模式都接受两种形态，差别只在是否对扁平兜底告警，因此不会出现今日能读的文件改版后读不了。
    /// - 形状包装（<c>{ kind, shape }</c>）的 kind 由 <see cref="BoardInkItemCodec"/> 校验，未知 kind 在 Codec 层跳过单条、不阻断；带形状条目的文件被旧 App 读取时扁平解析失败即拒读，语义不变。
    /// - Kind 为 null/缺省时归一为 <see cref="BoardInkItemCodec.StrokeKind"/>（仅 stroke 分支），不依赖 C# 属性初始化器；本转换器只做数据形态适配，值换算由 <see cref="BoardInkItemCodec"/> 负责。
    /// </remarks>
    internal sealed class InkItemSnapshotJsonConverter : JsonConverter<InkItemSnapshot>
    {
        private readonly InkItemReadMode _readMode;
        private readonly Action? _onUnexpectedLegacyFlatItem;

        /// <summary>
        /// 供类型特性 <c>[JsonConverter(typeof(InkItemSnapshotJsonConverter))]</c> 使用的默认构造：
        /// 按 v3（包装）模式读取，扁平兜底不告警。
        /// </summary>
        public InkItemSnapshotJsonConverter()
            : this(InkItemReadMode.Wrapped, onUnexpectedLegacyFlatItem: null)
        {
        }

        /// <summary>
        /// 按版本契约指定读模式；<paramref name="onUnexpectedLegacyFlatItem"/> 在该模式下遇到扁平条目时调用。
        /// </summary>
        public InkItemSnapshotJsonConverter(InkItemReadMode readMode, Action? onUnexpectedLegacyFlatItem = null)
        {
            _readMode = readMode;
            _onUnexpectedLegacyFlatItem = onUnexpectedLegacyFlatItem;
        }

        public override InkItemSnapshot? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("笔迹条目必须是 JSON 对象。");
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            // 大小写不敏感扫描：kind / stroke / shape 三个字段（与 JsonOptions 的字段名大小写不敏感策略一致）。
            string? kindText = null;
            bool hasKind = false;
            JsonElement strokeElement = default;
            bool hasStrokeObject = false;
            JsonElement shapeElement = default;
            bool hasShapeObject = false;

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, "kind", StringComparison.OrdinalIgnoreCase))
                {
                    hasKind = true;
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        kindText = property.Value.GetString();
                    }
                    // kind 为 null 时保持 kindText = null，交由缺省归一处理。
                }
                else if (string.Equals(property.Name, "stroke", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        hasStrokeObject = true;
                        strokeElement = property.Value.Clone();
                    }
                    else
                    {
                        // "stroke": null 或类型错误属于 v3 格式损坏，直接抛出让页面解析失败。
                        throw new JsonException("笔迹条目的 stroke 字段必须是对象。");
                    }
                }
                else if (string.Equals(property.Name, "shape", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        hasShapeObject = true;
                        shapeElement = property.Value.Clone();
                    }
                    else
                    {
                        // "shape": null 或类型错误属于 v3 格式损坏，直接抛出让页面解析失败。
                        throw new JsonException("笔迹条目的 shape 字段必须是对象。");
                    }
                }
            }

            if (hasStrokeObject)
            {
                // v3 折线包装形态：{ kind, stroke: {...} }。
                return new InkItemSnapshot
                {
                    Kind = NormalizeKind(hasKind ? kindText : null),
                    Stroke = ParseStrokeSnapshot(strokeElement, options),
                };
            }

            if (hasShapeObject)
            {
                // v3 形状包装形态：{ kind, shape: {...} }。
                // kind 不在此归一：缺失或为 "stroke" 时由 Codec 按损坏数据 fail-fast，未知 kind 由 Codec 记 Warn 跳过单条，不阻断整个加载。
                return new InkItemSnapshot
                {
                    Kind = hasKind && kindText is not null ? kindText : string.Empty,
                    Shape = ParseShapeSnapshot(shapeElement, options),
                };
            }

            // 旧格式（v1/v2）扁平形态：整个对象即 StrokeSnapshot。
            // 说明：kind 存在但无 stroke 包装（例如手工编辑的文件）同样按扁平形态读取。
            StrokeSnapshot legacy = ParseStrokeSnapshot(root, options);
            if (_readMode == InkItemReadMode.Wrapped)
            {
                // v3 文件里出现扁平条目属于格式倒退：兜底读取，但由回调方保证「每个文件只告警一次」。
                _onUnexpectedLegacyFlatItem?.Invoke();
            }

            return new InkItemSnapshot
            {
                Kind = NormalizeKind(hasKind ? kindText : null),
                Stroke = legacy,
            };
        }

        public override void Write(Utf8JsonWriter writer, InkItemSnapshot value, JsonSerializerOptions options)
        {
            if (BoardInkItemCodec.TryGetShapeKind(value.Kind, out BoardShapeKind _))
            {
                if (value.Shape is null)
                {
                    // 快照构造错误属于编程错误：宁可保存时失败，也不落盘一条读取时会丢数据的记录。
                    throw new JsonException($"InkItemSnapshot.Shape 为空，无法序列化形状条目（kind='{value.Kind}'）。");
                }

                writer.WriteStartObject();
                writer.WriteString("kind", value.Kind.Trim());
                writer.WritePropertyName("shape");
                JsonSerializer.Serialize(writer, value.Shape, options);
                writer.WriteEndObject();
                return;
            }

            if (value.Stroke is null)
            {
                // 快照构造错误属于编程错误：宁可保存时失败，也不落盘一条读取时会丢数据的记录。
                throw new JsonException("InkItemSnapshot.Stroke 为空，无法序列化笔迹条目。");
            }

            writer.WriteStartObject();
            writer.WriteString("kind", string.IsNullOrWhiteSpace(value.Kind)
                ? BoardInkItemCodec.StrokeKind
                : value.Kind.Trim());
            writer.WritePropertyName("stroke");
            JsonSerializer.Serialize(writer, value.Stroke, options);
            writer.WriteEndObject();
        }

        private static StrokeSnapshot ParseStrokeSnapshot(JsonElement element, JsonSerializerOptions options)
        {
            StrokeSnapshot? snapshot = element.Deserialize<StrokeSnapshot>(options);
            if (snapshot is null || snapshot.Points is null)
            {
                throw new JsonException("笔迹条目缺少 points 数据。");
            }

            return snapshot;
        }

        private static ShapeSnapshot ParseShapeSnapshot(JsonElement element, JsonSerializerOptions options)
        {
            ShapeSnapshot? snapshot = element.Deserialize<ShapeSnapshot>(options);
            if (snapshot is null)
            {
                throw new JsonException("笔迹条目缺少 shape 数据。");
            }

            return snapshot;
        }

        private static string NormalizeKind(string? kind)
        {
            return string.IsNullOrWhiteSpace(kind) ? BoardInkItemCodec.StrokeKind : kind.Trim();
        }
    }
}
