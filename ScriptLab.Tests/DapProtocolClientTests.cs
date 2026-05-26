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
    public void SendRequestPending_AllowsLaterRequestsBeforeWaitingForFirstResponse()
    {
        using var input = new MemoryStream();
        WriteFrame(input, new JsonObject
        {
            ["seq"] = 3,
            ["type"] = "response",
            ["request_seq"] = 2,
            ["success"] = true,
            ["command"] = "configurationDone"
        });
        WriteFrame(input, new JsonObject
        {
            ["seq"] = 4,
            ["type"] = "response",
            ["request_seq"] = 1,
            ["success"] = true,
            ["command"] = "launch"
        });
        input.Position = 0;
        using var output = new MemoryStream();
        using var client = new DapProtocolClient(input, output, leaveOpen: true);

        var launchRequest = client.SendRequestPending("launch", new JsonObject());
        client.SendRequest("configurationDone", new JsonObject());
        launchRequest.Wait();

        output.Position = 0;
        var launch = ReadFrame(output);
        var configurationDone = ReadFrame(output);
        Assert.Equal(1, launch["seq"]!.GetValue<int>());
        Assert.Equal("launch", launch["command"]!.GetValue<string>());
        Assert.Equal(2, configurationDone["seq"]!.GetValue<int>());
        Assert.Equal("configurationDone", configurationDone["command"]!.GetValue<string>());
    }

    [Fact]
    public void DrainEvents_WhenEventArrivesWithoutRequest_ReturnsEvent()
    {
        using var input = new BlockingStream();
        using var output = new MemoryStream();
        using var client = new DapProtocolClient(input, output, leaveOpen: true);

        WriteFrame(input, new JsonObject
        {
            ["seq"] = 7,
            ["type"] = "event",
            ["event"] = "stopped",
            ["body"] = new JsonObject
            {
                ["reason"] = "breakpoint"
            }
        });

        var @event = Assert.Single(DrainEventsEventually(client));
        Assert.Equal("stopped", @event["event"]!.GetValue<string>());
    }

    [Fact]
    public void FromInitializeResponseBody_ReadsBreakpointCapabilities()
    {
        var capabilities = DapBreakpointBackendCapabilities.FromInitializeResponseBody(new JsonObject
        {
            ["supportsConditionalBreakpoints"] = true,
            ["supportsHitConditionalBreakpoints"] = true,
            ["supportsBreakpointLocationsRequest"] = true,
            ["supportsInstructionBreakpoints"] = true
        });

        Assert.True(capabilities.SupportsConditionalBreakpoints);
        Assert.True(capabilities.SupportsHitConditionalBreakpoints);
        Assert.True(capabilities.SupportsBreakpointLocationsRequest);
        Assert.True(capabilities.SupportsInstructionBreakpoints);
    }

    [Fact]
    public void FromInitializeResponseBody_WhenCapabilitiesAreMissing_DefaultsToFalse()
    {
        var capabilities = DapBreakpointBackendCapabilities.FromInitializeResponseBody(new JsonObject());

        Assert.False(capabilities.SupportsConditionalBreakpoints);
        Assert.False(capabilities.SupportsHitConditionalBreakpoints);
        Assert.False(capabilities.SupportsBreakpointLocationsRequest);
        Assert.False(capabilities.SupportsInstructionBreakpoints);
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

    private static IReadOnlyList<JsonObject> DrainEventsEventually(DapProtocolClient client)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var events = client.DrainEvents();
            if (events.Count > 0)
            {
                return events;
            }

            Thread.Sleep(10);
        }

        return client.DrainEvents();
    }

    private sealed class BlockingStream : Stream
    {
        private readonly object syncRoot = new();
        private readonly Queue<byte> bytes = new();
        private bool disposed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (syncRoot)
            {
                while (bytes.Count == 0 && !disposed)
                {
                    Monitor.Wait(syncRoot);
                }

                if (bytes.Count == 0)
                {
                    return 0;
                }

                var read = Math.Min(count, bytes.Count);
                for (var index = 0; index < read; index++)
                {
                    buffer[offset + index] = bytes.Dequeue();
                }

                return read;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (syncRoot)
            {
                for (var index = 0; index < count; index++)
                {
                    bytes.Enqueue(buffer[offset + index]);
                }

                Monitor.PulseAll(syncRoot);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            lock (syncRoot)
            {
                disposed = true;
                Monitor.PulseAll(syncRoot);
            }

            base.Dispose(disposing);
        }
    }
}
