using ScriptLab;
using System.Text;
using System.Text.Json.Nodes;

namespace ScriptLab.Tests;

public sealed class DapProtocolClientTests
{
    [Fact]
    public void SendRequest_WhenResponseArrives_WritesFramedRequestAndReturnsBody()
    {
        using var input = new MemoryStream();
        WriteFrame(input, new JsonObject
        {
            ["seq"] = 7,
            ["type"] = "event",
            ["event"] = "initialized"
        });
        WriteFrame(input, new JsonObject
        {
            ["seq"] = 8,
            ["type"] = "response",
            ["request_seq"] = 1,
            ["success"] = true,
            ["command"] = "setBreakpoints",
            ["body"] = new JsonObject
            {
                ["breakpoints"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["verified"] = true,
                        ["line"] = 12
                    }
                }
            }
        });
        input.Position = 0;
        using var output = new MemoryStream();
        using var client = new DapProtocolClient(input, output, leaveOpen: true);

        var body = client.SendRequest(
            "setBreakpoints",
            new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["path"] = "PlayerMove.ash.cs"
                },
                ["breakpoints"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["line"] = 12,
                        ["column"] = 9
                    }
                }
            });

        var returnedBreakpoint = Assert.Single(body["breakpoints"]!.AsArray())!.AsObject();
        Assert.True(returnedBreakpoint["verified"]!.GetValue<bool>());

        output.Position = 0;
        var request = ReadFrame(output);
        Assert.Equal(1, request["seq"]!.GetValue<int>());
        Assert.Equal("request", request["type"]!.GetValue<string>());
        Assert.Equal("setBreakpoints", request["command"]!.GetValue<string>());
        Assert.Equal(12, request["arguments"]!
            .AsObject()["breakpoints"]!
            .AsArray()[0]!
            .AsObject()["line"]!
            .GetValue<int>());

        var @event = Assert.Single(client.DrainEvents());
        Assert.Equal("initialized", @event["event"]!.GetValue<string>());
    }

    [Fact]
    public void SendRequest_WhenResponseFails_ThrowsProtocolException()
    {
        using var input = new MemoryStream();
        WriteFrame(input, new JsonObject
        {
            ["seq"] = 2,
            ["type"] = "response",
            ["request_seq"] = 1,
            ["success"] = false,
            ["command"] = "initialize",
            ["message"] = "Adapter rejected initialize."
        });
        input.Position = 0;
        using var output = new MemoryStream();
        using var client = new DapProtocolClient(input, output, leaveOpen: true);

        var exception = Assert.Throws<DapProtocolException>(() =>
            client.SendRequest("initialize", new JsonObject()));
        Assert.Contains("Adapter rejected initialize", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromInitializeResponseBody_ReadsBreakpointCapabilities()
    {
        var capabilities = DapBreakpointBackendCapabilities.FromInitializeResponseBody(new JsonObject
        {
            ["supportsConditionalBreakpoints"] = true,
            ["supportsHitConditionalBreakpoints"] = true
        });

        Assert.True(capabilities.SupportsConditionalBreakpoints);
        Assert.True(capabilities.SupportsHitConditionalBreakpoints);
    }

    private static void WriteFrame(Stream stream, JsonObject message)
    {
        var body = message.ToJsonString();
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
        stream.Write(headerBytes);
        stream.Write(bodyBytes);
    }

    private static JsonObject ReadFrame(Stream stream)
    {
        var headerBytes = new List<byte>();
        while (true)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                throw new EndOfStreamException();
            }

            headerBytes.Add((byte)next);
            if (headerBytes.Count >= 4 &&
                headerBytes[^4] == (byte)'\r' &&
                headerBytes[^3] == (byte)'\n' &&
                headerBytes[^2] == (byte)'\r' &&
                headerBytes[^1] == (byte)'\n')
            {
                break;
            }
        }

        var header = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lengthLine = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var length = int.Parse(
            lengthLine["Content-Length:".Length..].Trim(),
            System.Globalization.CultureInfo.InvariantCulture);
        var bodyBytes = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(bodyBytes, offset, length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return JsonNode.Parse(Encoding.UTF8.GetString(bodyBytes))!.AsObject();
    }
}
