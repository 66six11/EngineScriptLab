using System.Security.Cryptography;
using System.Text;

namespace ScriptLab;

public sealed record SourceAnchor(string FileName, int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record GraphNodeIdentity(
    string FunctionName,
    string NodeKind,
    string? BoundId,
    SourceAnchor SourceAnchor,
    int OrdinalAmongSiblings,
    string SourceTextHash)
{
    public static GraphNodeIdentity FromSource(
        string functionName,
        string nodeKind,
        string? boundId,
        string source,
        string fileName,
        int start,
        int length,
        int ordinalAmongSiblings = 0)
    {
        return new GraphNodeIdentity(
            functionName,
            nodeKind,
            boundId,
            new SourceAnchor(fileName, start, length),
            ordinalAmongSiblings,
            ComputeSourceTextHash(source, start, length));
    }

    public static string ComputeSourceTextHash(string source, int start, int length)
    {
        if (start < 0 || length < 0 || start + length > source.Length)
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(source.AsSpan(start, length).ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
