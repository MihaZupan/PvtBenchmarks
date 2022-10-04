using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading.Tasks.Sources;

static class Program
{
    public static long CombinedRequests = 0;

    public static void Main(string[] args)
    {
        Console.WriteLine(FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location).ProductVersion);
        Console.WriteLine(Environment.ProcessId);

        //WebSocketAllocationTest().GetAwaiter().GetResult();

        //var benchmark = new IndexOfAnyBenchmarks();
        //benchmark.Setup();
        //RunForProfiler(1, Timeout.InfiniteTimeSpan, benchmark, b => b.IndexOfAny());

        //BenchmarkSwitcher.FromAssembly(typeof(HttpClientBenchmarks).Assembly).Run(args);
        BenchmarkRunner.Run<IndexOfAnyBenchmarks>(args: args);



        //var benchmark = new HttpClientBenchmarks
        //{
        //    RequestHeaders = 0,
        //    ConcurrencyPerHandler = 1,
        //};
        //benchmark.Setup();
        //RunForProfiler(1, Timeout.InfiniteTimeSpan, benchmark, b => b.SendAsync());

        //for (int i = 1; i < 8; i++)
        //{
        //    RunForProfiler(i, TimeSpan.FromSeconds(30));
        //}
    }

    static async Task WebSocketAllocationTest()
    {
        var uri = new Uri("ws://corefx-net-http11.azurewebsites.net/WebSocket/EchoWebSocket.ashx");

        while (true)
        {
            for (int i = 0; i < 10; i++)
            {
                using var cws = new ClientWebSocket();
                await cws.ConnectAsync(uri, CancellationToken.None);
            }

            Console.WriteLine("Sleeping ...");
            await Task.Delay(1000);
        }
    }

    static void RunForProfiler<TBenchmark>(int numWorkers, TimeSpan runTime, TBenchmark benchmark, Action<TBenchmark> testAction)
    {
        using var cts = new CancellationTokenSource(runTime);

        Task.Run(async () =>
        {
            await Task.Delay(10_000);

            Stopwatch s = Stopwatch.StartNew();
            Volatile.Write(ref CombinedRequests, 0);

            while (!cts.IsCancellationRequested)
            {
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(1000);
                    long numRequests = Volatile.Read(ref CombinedRequests);
                    Console.Title = $"{numWorkers}: {(int)(numRequests / s.Elapsed.TotalSeconds / 1000)} k/s";
                }

                long numRequestsTotal = Volatile.Read(ref CombinedRequests);
                Console.WriteLine($"{numWorkers}: {(int)(numRequestsTotal / s.Elapsed.TotalSeconds / 1000)} k/s");
                //s.Restart();
            }
        });

        var threads = Enumerable.Range(1, numWorkers - 1)
            .Select(i => new Thread(() => Worker(benchmark, testAction, $"{(char)(i + 'A')}", cts.Token)) { IsBackground = true })
            .ToArray();

        foreach (var t in threads) t.Start();

        Worker(benchmark, testAction, "A", cts.Token);

        foreach (var t in threads) t.Join();

        static void Worker(TBenchmark benchmark, Action<TBenchmark> testAction, string name, CancellationToken cancellationToken)
        {
            //const int LoopIterations = 1_000;
            const int LoopIterations = 100_000;

            Stopwatch s = Stopwatch.StartNew();

            while (!cancellationToken.IsCancellationRequested)
            {
                for (int i = 0; i < LoopIterations; i++)
                {
                    WorkLoop(benchmark, testAction, LoopIterations);
                    Interlocked.Add(ref CombinedRequests, LoopIterations);
                }

                //Console.WriteLine($"{name}: {(int)((LoopIterations * LoopIterations) / s.Elapsed.TotalSeconds / 1000)} k/s");
                s.Restart();
            }

            static void WorkLoop(TBenchmark benchmark, Action<TBenchmark> testAction, int iterations)
            {
                for (int i = 0; i < iterations; i++)
                {
                    testAction(benchmark);
                }
            }
        }
    }
}

public class SyncVsAsyncHttpClient
{
    private readonly Uri _requestUri = new("http://127.0.0.1:8080/plaintext");
    private readonly HttpClient _httpClient = new();

    private HttpRequestMessage CreateRequest() => new(HttpMethod.Get, _requestUri)
    {
        Version = HttpVersion.Version11,
        VersionPolicy = HttpVersionPolicy.RequestVersionExact
    };

    [Benchmark]
    public async Task SendAsync()
    {
        using HttpResponseMessage response = await _httpClient.SendAsync(CreateRequest(), CancellationToken.None);
    }

    [Benchmark]
    public void Send()
    {
        using HttpResponseMessage response = _httpClient.Send(CreateRequest(), CancellationToken.None);
    }
}

//[MemoryDiagnoser]
//[LongRunJob]
public class HttpClientBenchmarks
{
    private static readonly Uri _requestUri = new("http://10.0.0.100:8080/plaintext");
    private HttpMessageInvoker _messageInvoker = null!;
    private HttpClient _httpClient = null!;
    private string[] _requestHeaders = null!;
    private HttpRequestMessage? _request;

    public int ContentLength = 32;

    //[Params(
    //    0, 1, 2, 3, 4, 5, 6, 7, 8
    //    , 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 26, 28, 30, 32, 40, 48, 56, 64, 100
    //    )]
    //[Params(0, 1, 4, 8, 16)]
    public int RequestHeaders = 0;

    [Params(1, 2, 3, 4, 8, 16, 32, 64)]
    public int ResponseHeaders = 1;

    public bool UsePreparedRequest = true;

    public bool UseHttpClient = false;

    public int ConcurrencyPerHandler = 1;

    private HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _requestUri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        foreach (string headerName in _requestHeaders)
        {
            request.Headers.TryAddWithoutValidation(headerName, "foo-bar-123");
        }

        return request;
    }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);

        byte[] responseBytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            //"Date: Sun, 12 Dec 2021 22:23:40 GMT\r\n" +
            //"Server: Kestrel\r\n" +
            //"Content-type: text/html\r\n" +
            $"Content-Length: {ContentLength}\r\n" +
            string.Concat(HttpHeadersBenchmarks.CreateResponseHeaders(ResponseHeaders - 1).Select(name => $"{name}: {new string('a', rng.Next(8, 64))}\r\n")) +
            "\r\n" +
            new string('a', ContentLength));

        var connectCallbackLock = new CountdownEvent(ConcurrencyPerHandler);

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            //ActivityHeadersPropagator = null,
            PooledConnectionIdleTimeout = TimeSpan.FromDays(10), // Avoid the cleaning timer executing during the benchmark
            ConnectCallback = (context, cancellation) =>
            {
                connectCallbackLock.Signal();
                connectCallbackLock.Wait(cancellation);
                return new ValueTask<Stream>(new ResponseStream(responseBytes));
            }
        };

        _messageInvoker = new HttpMessageInvoker(handler);
        _httpClient = new HttpClient(handler);

        _requestHeaders = HttpHeadersBenchmarks.CreateRequestHeaderNames(RequestHeaders);

        _request = CreateRequest();

        Task.WaitAll(Enumerable.Repeat(0, ConcurrencyPerHandler)
            .Select(_ => Task.Run(async () =>
            {
                using HttpResponseMessage response = await _messageInvoker.SendAsync(_request, CancellationToken.None);
                await response.Content.CopyToAsync(Stream.Null);
            }))
            .ToArray());

        if (!UsePreparedRequest)
        {
            _request = null;
        }
    }

    [Benchmark]
    public void SendAsync()
    {
        HttpRequestMessage request = _request ?? CreateRequest();

        Task<HttpResponseMessage> responseTask = UseHttpClient
            ? _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)
            : _messageInvoker.SendAsync(request, CancellationToken.None);

        if (!responseTask.IsCompletedSuccessfully)
        {
            throw new Exception();
        }

        using HttpResponseMessage response = responseTask.Result;

        if (ContentLength > 0)
        {
            Task copyToTask = response.Content.CopyToAsync(Stream.Null);

            if (!copyToTask.IsCompletedSuccessfully)
            {
                throw new Exception();
            }

            copyToTask.GetAwaiter().GetResult();
        }
    }
}

public sealed class ResponseStream : Stream, IValueTaskSource<int>
{
    private ManualResetValueTaskSourceCore<int> _waitSource = new() { RunContinuationsAsynchronously = true };
    private bool _writeCompleted;
    private bool _readStarted;

    private readonly byte[] _responseData;

    public ResponseStream(byte[] responseData)
    {
        _responseData = responseData;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _responseData.CopyTo(buffer.Span);

        lock (this)
        {
            if (_writeCompleted)
            {
                _writeCompleted = false;
                return new ValueTask<int>(_responseData.Length);
            }
            else
            {
                _readStarted = true;
                _waitSource.Reset();
                return new ValueTask<int>(this, _waitSource.Version);
            }
        }
    }

    public override int Read(Span<byte> buffer)
    {
        Debug.Assert(_writeCompleted);
        _writeCompleted = false;
        _responseData.CopyTo(buffer);
        return _responseData.Length;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        lock (this)
        {
            if (_readStarted)
            {
                _readStarted = false;
                _waitSource.SetResult(_responseData.Length);
            }
            else
            {
                _writeCompleted = true;
            }
        }
        return default;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Debug.Assert(!_readStarted);
        _writeCompleted = true;
    }

    public int GetResult(short token) => _waitSource.GetResult(token);

    public ValueTaskSourceStatus GetStatus(short token) => _waitSource.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _waitSource.OnCompleted(continuation, state, token, flags);

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override void Flush() => throw new InvalidOperationException();
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException();
    public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException();
    public override void SetLength(long value) => throw new InvalidOperationException();
    public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException();
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new InvalidOperationException();
    public override long Position { get => throw new InvalidOperationException(); set => throw new InvalidOperationException(); }
}

[MemoryDiagnoser]
public class HttpHeadersBenchmarks
{
    private static readonly Action<HttpHeaders, HttpHeaders> _addHeadersMethod =
        typeof(HttpHeaders).GetMethod("AddHeaders", BindingFlags.NonPublic | BindingFlags.Instance)!
        .CreateDelegate<Action<HttpHeaders, HttpHeaders>>();

    [Params(2, 4, 6, 8, 12, 16, 32)]
    public int RequestHeaders;

    private string[] _requestHeaderNames = null!;

    private const string HeaderValue = "foo-bar-123";

    private HttpHeaders _headers = null!;

    [GlobalSetup]
    public void Setup()
    {
        _requestHeaderNames = CreateRequestHeaderNames(RequestHeaders);

        var request = new HttpRequestMessage();
        foreach (var header in _requestHeaderNames)
        {
            request.Headers.TryAddWithoutValidation(header, HeaderValue);
        }
        _headers = request.Headers;
    }

    public static string[] CreateRequestHeaderNames(int numberOfHeaders)
    {
        var headerNames = new List<string>
        {
            // Known headers without specific value format requirements
            "Cookie",
            "Set-Cookie",
            "Age",
            "Origin",
            "ETag",
            "Server",
            "TE",
            "X-Request-ID"
        };

        var buffer = new byte[64];
        var rng = new Random(42);

        while (headerNames.Count < numberOfHeaders)
        {
            rng.NextBytes(buffer);
            string base64 = Convert.ToBase64String(buffer).TrimEnd('=').Replace("+", "").Replace("/", "");
            int length = rng.Next(8, 32);
            string name = base64.Substring(0, length);

            if (!headerNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                headerNames.Add(name);
            }
        }

        foreach (var headerName in headerNames)
        {
            var request = new HttpRequestMessage();
            try
            {
                request.Headers.Add(headerName, HeaderValue);
            }
            catch
            {
                throw new Exception($"Invalid header '{headerName}: {HeaderValue}'");
            }
        }

        return headerNames.ToArray().AsSpan(0, numberOfHeaders).ToArray();
    }

    public static string[] CreateResponseHeaders(int numberOfHeaders)
    {
        var headerNames = new List<string>
        {
            // Known headers without specific value format requirements
            "Age",
            "Origin",
            "Cookie",
            "Set-Cookie",
            "ETag",
            "Server",
            "TE",
            "X-Request-ID"
        };

        var buffer = new byte[64];
        var rng = new Random(42);

        while (headerNames.Count < numberOfHeaders)
        {
            rng.NextBytes(buffer);
            string base64 = Convert.ToBase64String(buffer).TrimEnd('=').Replace("+", "").Replace("/", "");
            int length = rng.Next(8, 32);
            string name = base64.Substring(0, length);

            if (!headerNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                headerNames.Add(name);
            }
        }

        foreach (var headerName in headerNames)
        {
            var request = new HttpRequestMessage();
            try
            {
                request.Headers.Add(headerName, "aaaaaa");
            }
            catch
            {
                throw new Exception($"Invalid header '{headerName}: {HeaderValue}'");
            }
        }

        return headerNames.ToArray().AsSpan(0, numberOfHeaders).ToArray();
    }

    //[Benchmark]
    public void AddHeaders()
    {
        var request = new HttpRequestMessage();
        _addHeadersMethod(request.Headers, _headers);
    }

    //[Benchmark]
    public HttpRequestMessage NonValidatedAdd()
    {
        var request = new HttpRequestMessage();
        HttpRequestHeaders headers = request.Headers;

        foreach (string name in _requestHeaderNames)
        {
            headers.TryAddWithoutValidation(name, HeaderValue);
        }

        return request;
    }

    //[Benchmark]
    public HttpRequestMessage ValidatedAdd()
    {
        var request = new HttpRequestMessage();
        HttpRequestHeaders headers = request.Headers;

        foreach (string name in _requestHeaderNames)
        {
            headers.Add(name, HeaderValue);
        }

        return request;
    }

    //[Benchmark]
    //public int NonValidatedAdd_NonValidatedEnumerate()
    //{
    //    var request = new HttpRequestMessage();
    //    HttpRequestHeaders headers = request.Headers;

    //    foreach (string name in _headerNames)
    //    {
    //        headers.TryAddWithoutValidation(name, HeaderValue);
    //    }

    //    int count = 0;
    //    foreach (KeyValuePair<string, HeaderStringValues> header in headers.NonValidated)
    //    {
    //        count++;
    //    }
    //    return count;
    //}

    //[Benchmark]
    public int NonValidatedAdd_ValidatedEnumerate()
    {
        var request = new HttpRequestMessage();
        HttpRequestHeaders headers = request.Headers;

        foreach (string name in _requestHeaderNames)
        {
            headers.TryAddWithoutValidation(name, HeaderValue);
        }

        int count = 0;
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            count++;
        }
        return count;
    }

    ////[Benchmark]
    //public int ValidatedAdd_NonValidatedEnumerate()
    //{
    //    var request = new HttpRequestMessage();
    //    HttpRequestHeaders headers = request.Headers;

    //    foreach (string name in _headerNames)
    //    {
    //        headers.Add(name, HeaderValue);
    //    }

    //    int count = 0;
    //    foreach (KeyValuePair<string, HeaderStringValues> header in headers.NonValidated)
    //    {
    //        count++;
    //    }
    //    return count;
    //}

    //[Benchmark]
    public int ValidatedAdd_ValidatedEnumerate()
    {
        var request = new HttpRequestMessage();
        HttpRequestHeaders headers = request.Headers;

        foreach (string name in _requestHeaderNames)
        {
            headers.Add(name, HeaderValue);
        }

        int count = 0;
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            count++;
        }
        return count;
    }
}

public class HttpHeadersWorstCase
{
    [Params(16, 32, 64, 128)]
    public int ResponseHeadersLengthKb;

    [Params(64, 128, 256, 512, 1024)]
    public int NumberOfHeaders;

    private string[] _headerNames = null!;

    [GlobalSetup]
    public void Setup()
    {
        int responseLength = ResponseHeadersLengthKb * 1024;
        int maxBytesPerHeader = responseLength / NumberOfHeaders;
        int maxBytesPerName = maxBytesPerHeader - 4; // ':', ' ', '\r', '\n'

        var nameBytes = new char[maxBytesPerName];
        Array.Fill(nameBytes, 'a');

        var headerNames = new string[NumberOfHeaders];
        for (int i = 0; i < headerNames.Length; i++)
        {
            Span<char> uniqueSuffix = nameBytes.AsSpan(nameBytes.Length - 4);
            uniqueSuffix.Fill('a');

            if (i < 10) uniqueSuffix = uniqueSuffix.Slice(3);
            else if (i < 100) uniqueSuffix = uniqueSuffix.Slice(2);
            else if (i < 1000) uniqueSuffix = uniqueSuffix.Slice(1);

            i.TryFormat(uniqueSuffix, out _);

            headerNames[i] = new string(nameBytes);
        }

        _headerNames = headerNames;
    }

    [Benchmark]
    public void Add()
    {
        var request = new HttpRequestMessage();
        HttpRequestHeaders headers = request.Headers;

        foreach (string name in _headerNames)
        {
            headers.TryAddWithoutValidation(name, "");
        }
    }
}

//[DisassemblyDiagnoser]
public class IndexOfAnyBenchmarks
{
    private string _text = null!;
    private string _textExcept = null!;
    private string _needle = null!;

    [Params(1, 8, 16, 32, 64, 128, 10000)]
    public int Length = 1;

    [Params("ABCDEF", "AlphaNumeric", "ValidUriChars")]
    //[Params("ABCDEF")]
    public string? Needle = "ABCDEF";

    [GlobalSetup]
    public void Setup()
    {
        _text = new string('\n', Length);
        _textExcept = new string('a', Length);

        const string AlphaNumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

        _needle = Needle switch
        {
            "ABCDEF" => "abcdef",
            "AlphaNumeric" => AlphaNumeric,
            // Similar thing in YARP: https://github.com/microsoft/reverse-proxy/blob/5a86eb4f18474bbe2f9e73f8457f4cbf13c00b81/src/ReverseProxy/Forwarder/RequestUtilities.cs#L212-L228
            "ValidUriChars" => AlphaNumeric + "-._~" + ":/?#[]@" + "!$&'()*+,;=",
            "NeedleWithZero" => "abcde" + '\0',
            _ => throw new Exception(Needle)
        };
    }

    [Benchmark]
    public int IndexOfAny() => _text.AsSpan().IndexOfAny(_needle);

    //[Benchmark]
    public int IndexOfAnyExcept() => _textExcept.AsSpan().IndexOfAnyExcept(_needle);

    //[Benchmark]
    public int LastIndexOfAny() => _text.AsSpan().LastIndexOfAny(_needle);

    //[Benchmark]
    public int LastIndexOfAnyExcept() => _textExcept.AsSpan().LastIndexOfAnyExcept(_needle);

    //[Benchmark]
    public int IndexOfAnyWithZero() => _text.AsSpan().IndexOfAny("abcde" + '\0');
}