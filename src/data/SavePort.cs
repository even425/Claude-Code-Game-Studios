using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeCodeGameStudios.Data;

/// <summary>
/// 存档端口 —— 写盘、读盘、校验。
///
/// <remarks>
/// <para><b>本类不解释存档内容。</b>它只负责「把 <see cref="SaveGame"/> 变成一个文件」
/// 和「把一个文件变回 <see cref="SaveGame"/> 并验一遍」。
/// 怎么把运行中的状态<b>装进</b> <see cref="SaveGame"/>、怎么把它<b>还原</b>回去，
/// 由调用方决定 —— 那需要认识村庄、英雄、任务与结算器，端口不该认识这些。</para>
///
/// <para><b>为什么用 <see cref="System.Text.Json"/> 而不是 Godot 的
/// <c>FileAccess</c> + <c>JSON</c></b>：Godot 的 JSON 是 <c>Variant</c> 形状的，
/// 与这些强类型 DTO 对不上；而 <c>System.Text.Json</c> 在 .NET 里，
/// 不给表现层引入依赖（领域层不得 <c>using Godot</c>，本类同样不该）。
/// 落盘路径也由调用方注入，不写死 <c>user://</c>。</para>
///
/// <para><b>写盘是原子的。</b>先写临时文件再替换 —— 存档写到一半掉电，
/// 留下的应该是一个完整的旧存档，而不是一个半截的新存档。
/// <b>读盘不做部分恢复</b>：校验不过就把整份存档交回去由调用方拒绝
/// （不变量 I11），绝不做「能救几个救几个」。</para>
/// </remarks>
/// </summary>
public sealed class SavePort
{
    /// <summary>默认文件名。</summary>
    public const string DefaultFileName = "village-save.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 存档里有 double（模拟时间），读写往返要允许精度损失，
        // 否则一次读档就会把时间改掉一点点 —— 那种漂移极难察觉。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    /// <summary>构造一个存档端口。</summary>
    /// <param name="path">存档文件路径，非空。</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空或全空白。</exception>
    public SavePort(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("存档路径不能为空。", nameof(path));
        }

        _path = path;
    }

    /// <summary>存档文件路径。</summary>
    public string FilePath => _path;

    /// <summary>存档文件是否已存在。</summary>
    public bool Exists => File.Exists(_path);

    /// <summary>
    /// 写盘。<b>原子</b>：先写临时文件，再替换目标。
    /// </summary>
    /// <param name="save">要写的存档，非空。</param>
    /// <exception cref="ArgumentNullException"><paramref name="save"/> 为 null。</exception>
    /// <exception cref="IOException">写入失败（磁盘满、权限不足等）。</exception>
    public void Write(SaveGame save)
    {
        if (save is null) throw new ArgumentNullException(nameof(save));

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(save, Options));

        // 只有临时文件完整写出来了才替换 —— 写到一半掉电时，
        // 原存档仍然完好，而不是留下半个文件。
        File.Move(temp, _path, overwrite: true);
    }

    /// <summary>读盘并校验的结果。</summary>
    /// <param name="Save">读出并<b>通过校验</b>的存档；否则为 <c>null</c>。</param>
    /// <param name="Validation">校验结果，含错误清单。</param>
    /// <param name="ReadError">读取/反序列化层面的错误（文件不存在、JSON 损坏等）。</param>
    public readonly record struct LoadOutcome(
        SaveGame? Save,
        SaveValidationResult Validation,
        string? ReadError)
    {
        /// <summary>是否真的可以用了 —— 读到、且校验全过。</summary>
        public bool Loaded => Save is not null && ReadError is null && Validation.IsValid;

        /// <summary>一句人话，说明为什么不能用。</summary>
        public string Describe() => ReadError is not null
            ? ReadError
            : Validation.Describe();
    }

    /// <summary>
    /// 读盘。<b>不做部分恢复</b>（不变量 I11）——
    /// 校验不通过时 <see cref="LoadOutcome.Save"/> 为 <c>null</c>，
    /// 由调用方整份拒绝。
    /// </summary>
    public LoadOutcome Read()
    {
        if (!File.Exists(_path))
        {
            return new LoadOutcome(
                null,
                SaveValidationResult.Fail(new[] { new SaveError(_path, "存档文件不存在。") }),
                $"存档文件不存在：{_path}");
        }

        SaveGame? save;
        try
        {
            save = JsonSerializer.Deserialize<SaveGame>(File.ReadAllText(_path), Options);
        }
        catch (JsonException ex)
        {
            // JSON 坏了就是坏了 —— 不尝试「捞出还能用的字段」。
            return new LoadOutcome(
                null,
                SaveValidationResult.Fail(new[] { new SaveError(_path, $"存档无法解析：{ex.Message}") }),
                $"存档无法解析：{ex.Message}");
        }
        catch (IOException ex)
        {
            return new LoadOutcome(
                null,
                SaveValidationResult.Fail(new[] { new SaveError(_path, $"存档读取失败：{ex.Message}") }),
                $"存档读取失败：{ex.Message}");
        }

        var validation = SaveValidator.Validate(save);

        // 校验不过就整份退回 —— 哪怕只有一条引用断了，也不给半份。
        return validation.IsValid
            ? new LoadOutcome(save, validation, null)
            : new LoadOutcome(null, validation, null);
    }

    /// <summary>删掉存档文件。文件不存在时<b>不报错</b>（幂等删除）。</summary>
    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
        string temp = _path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
    }
}
