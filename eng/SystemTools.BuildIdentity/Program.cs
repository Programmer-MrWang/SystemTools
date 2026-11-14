using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

static (int Timestamp, int Mvid) Locate(byte[] bytes)
{
    using var stream = new MemoryStream(bytes, writable: false);
    using var pe = new PEReader(stream);
    var headers = pe.PEHeaders;
    if (headers.CorHeader is null || headers.PEHeader is null || !pe.HasMetadata ||
        headers.CorHeader.Flags.HasFlag(CorFlags.StrongNameSigned) ||
        headers.CorHeader.StrongNameSignatureDirectory.Size != 0 ||
        headers.PEHeader.CertificateTableDirectory.Size != 0 || headers.PEHeader.CheckSum != 0 ||
        pe.ReadDebugDirectory().Any(entry => entry.Type != DebugDirectoryEntryType.Reproducible))
        throw new InvalidDataException("仅规范没有签名或调试符号的托管入口。");
    var metadata = pe.GetMetadataReader();
    var module = metadata.GetModuleDefinition();
    var assembly = metadata.GetAssemblyDefinition();
    if (metadata.GetString(module.Name) != "SystemTools.dll" || metadata.GetString(assembly.Name) != "SystemTools")
        throw new InvalidDataException("仅允许规范 SystemTools 中立入口。");
    var mvid = checked(headers.MetadataStartOffset + metadata.GetHeapMetadataOffset(HeapIndex.Guid) +
                       (MetadataTokens.GetHeapOffset(module.Mvid) - 1) * 16);
    if (new Guid(bytes.AsSpan(mvid, 16)) != metadata.GetGuid(module.Mvid))
        throw new InvalidDataException("无法验证模块标识位置。");
    return (headers.CoffHeaderStartOffset + 4, mvid);
}

static byte[] Normalize(byte[] source)
{
    var result = source.ToArray();
    var (timestamp, mvid) = Locate(result);
    result.AsSpan(timestamp, 4).Clear();
    result.AsSpan(mvid, 16).Clear();
    // 用完整产物生成构建身份；其余字节保持原样，业务差异仍影响最终包哈希。
    var identity = BlobContentId.FromHash(SHA256.HashData(result));
    identity.Guid.TryWriteBytes(result.AsSpan(mvid, 16));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(timestamp, 4), identity.Stamp);
    return result;
}

if (args.Length == 2 && args[0] == "--verify")
{
    var source = File.ReadAllBytes(args[1]);
    var normal = Normalize(source);
    if (!Normalize(normal).SequenceEqual(normal)) throw new Exception("规范化不幂等。");
    var (timestamp, mvid) = Locate(source);
    var changedIdentity = source.ToArray();
    changedIdentity[timestamp] ^= 1;
    changedIdentity[mvid] ^= 1;
    if (!Normalize(changedIdentity).SequenceEqual(normal)) throw new Exception("构建身份仍影响结果。");
    var changedContent = source.ToArray();
    changedContent[^1] ^= 1;
    if (Normalize(changedContent).SequenceEqual(normal)) throw new Exception("内容差异被忽略。");
    Console.WriteLine("PASS 1: 入口身份规范幂等，仅忽略构建标识且保留其余内容差异");
    return;
}
if (args.Length == 0) throw new ArgumentException("请指定打包入口路径。");
foreach (var path in args)
{
    if (Path.GetFileName(path) != "SystemTools.dll") throw new ArgumentException("仅允许 SystemTools.dll。");
    var source = File.ReadAllBytes(path);
    var result = Normalize(source);
    File.WriteAllBytes(path, result);
    Console.WriteLine("Normalized entrance: " + Convert.ToHexString(SHA256.HashData(result)));
}
