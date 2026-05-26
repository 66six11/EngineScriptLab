using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScriptLab;

public sealed class DapProtocolException : Exception
{
    public DapProtocolException(string message)
        : base(message)
    {
    }
}

public sealed class DapProtocolClient : IDapRequestClient, IDapPendingRequestClient, IDapEventSource, IDisposable
{
    private readonly Stream input;
    private readonly Stream output;
    private readonly bool leaveOpen;
    private readonly object syncRoot = new();
    private readonly Queue<JsonObject> pendingEvents = new();
    private readonly Dictionary<int, JsonObject> pendingResponses = new();
    private readonly Thread readerThread;
    private int nextSequence = 1;
    private Exception? readerException;
    private bool readerCompleted;
    private bool disposed;

    public DapProtocolClient(Stream input, Stream output, bool leaveOpen = false)
    {
        this.input = input;
        this.output = output;
        this.leaveOpen = leaveOpen;
        readerThread = new Thread(ReadMessages)
        {
            IsBackground = true,
            Name = "ScriptLab DAP reader"
        };
        readerThread.Start();
    }

    public JsonObject SendRequest(string command, JsonObject arguments)
    {
        return SendRequestPending(command, arguments).Wait();
    }

    public DapPendingRequest SendRequestPending(string command, JsonObject arguments)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var sequence = WriteRequest(command, arguments);
        return new DapPendingRequest(() => WaitForResponse(sequence, command));
    }

    private int WriteRequest(string command, JsonObject arguments)
    {
        lock (syncRoot)
        {
            var sequence = nextSequence++;
            WriteMessage(new JsonObject
            {
                ["seq"] = sequence,
                ["type"] = "request",
                ["command"] = command,
                ["arguments"] = arguments.DeepClone()
            });
            return sequence;
        }
    }

    private JsonObject WaitForResponse(int sequence, string command)
    {
        lock (syncRoot)
        {
            JsonObject? message;
            while (!pendingResponses.Remove(sequence, out message))
            {
                if (readerException is not null)
                {
                    throw new DapProtocolException(
                        $"DAP reader failed before response for request '{command}': {readerException.Message}");
                }

                if (readerCompleted)
                {
                    throw new DapProtocolException(
                        $"DAP stream closed before response for request '{command}'.");
                }

                Monitor.Wait(syncRoot);
            }

            message ??= new JsonObject();

            var responseCommand = GetString(message, "command");
            if (!string.Equals(responseCommand, command, StringComparison.Ordinal))
            {
                throw new DapProtocolException(
                    $"DAP response command '{responseCommand}' did not match request command '{command}'.");
            }

            if (!GetBoolean(message, "success", defaultValue: true))
            {
                throw new DapProtocolException(
                    GetString(message, "message") ??
                    $"DAP request '{command}' failed.");
            }

            return message.TryGetPropertyValue("body", out var bodyNode) &&
                   bodyNode is JsonObject body
                ? body
                : new JsonObject();
        }
    }

    public IReadOnlyList<JsonObject> DrainEvents()
    {
        lock (syncRoot)
        {
            var events = pendingEvents.ToArray();
            pendingEvents.Clear();
            return events;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (!leaveOpen)
        {
            input.Dispose();
            output.Dispose();
        }

        lock (syncRoot)
        {
            readerCompleted = true;
            Monitor.PulseAll(syncRoot);
        }
    }

    private void ReadMessages()
    {
        try
        {
            while (true)
            {
                DispatchMessage(ReadMessage());
            }
        }
        catch (EndOfStreamException)
        {
            CompleteReader(null);
        }
        catch (ObjectDisposedException) when (disposed)
        {
            CompleteReader(null);
        }
        catch (Exception exception)
        {
            CompleteReader(exception);
        }
    }

    private void DispatchMessage(JsonObject message)
    {
        lock (syncRoot)
        {
            var type = GetString(message, "type");
            if (string.Equals(type, "event", StringComparison.Ordinal))
            {
                pendingEvents.Enqueue(message);
            }
            else if (string.Equals(type, "response", StringComparison.Ordinal))
            {
                var requestSequence = GetInt32(message, "request_seq");
                if (requestSequence is not null)
                {
                    pendingResponses[requestSequence.Value] = message;
                }
            }

            Monitor.PulseAll(syncRoot);
        }
    }

    private void CompleteReader(Exception? exception)
    {
        lock (syncRoot)
        {
            readerException = exception;
            readerCompleted = true;
            Monitor.PulseAll(syncRoot);
        }
    }

    private void WriteMessage(JsonObject message)
    {
        var body = message.ToJsonString();
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
        output.Write(headerBytes);
        output.Write(bodyBytes);
        output.Flush();
    }

    private JsonObject ReadMessage()
    {
        var headerBytes = ReadHeaderBytes();
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var contentLength = ReadContentLength(headerText);
        var bodyBytes = ReadExactly(contentLength);
        var bodyText = Encoding.UTF8.GetString(bodyBytes);
        return JsonNode.Parse(bodyText)?.AsObject()
            ?? throw new DapProtocolException("DAP message body must be a JSON object.");
    }

    private byte[] ReadHeaderBytes()
    {
        var bytes = new List<byte>();
        while (true)
        {
            var next = input.ReadByte();
            if (next < 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading DAP headers.");
            }

            bytes.Add((byte)next);
            if (EndsWithHeaderTerminator(bytes))
            {
                return bytes.ToArray();
            }
        }
    }

    private static bool EndsWithHeaderTerminator(IReadOnlyList<byte> bytes)
    {
        return bytes.Count >= 4 &&
               bytes[^4] == (byte)'\r' &&
               bytes[^3] == (byte)'\n' &&
               bytes[^2] == (byte)'\r' &&
               bytes[^1] == (byte)'\n';
    }

    private static int ReadContentLength(string headerText)
    {
        foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            if (int.TryParse(
                    value,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var contentLength) &&
                contentLength >= 0)
            {
                return contentLength;
            }

            throw new DapProtocolException($"Invalid DAP Content-Length value '{value}'.");
        }

        throw new DapProtocolException("DAP message is missing the Content-Length header.");
    }

    private byte[] ReadExactly(int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = input.Read(buffer, offset, length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading DAP message body.");
            }

            offset += read;
        }

        return buffer;
    }

    private static string? GetString(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node)
            ? node?.GetValue<string>()
            : null;
    }

    private static int? GetInt32(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValue<int>()
            : null;
    }

    private static bool GetBoolean(JsonObject json, string name, bool defaultValue)
    {
        return json.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValue<bool>()
            : defaultValue;
    }
}
