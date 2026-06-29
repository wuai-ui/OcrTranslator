using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OcrTranslator.Abstractions;
using OcrTranslator.Models;

namespace OcrTranslator.Services;

/// <summary>
/// 百度语音合成（TTS）引擎：HTTP + WebSocket 双协议。
/// WebSocket 遵循四步状态机，使用 ArrayPool 内存池防止 GC 爆炸。
/// 算法从 v3 BaiduApi 原样搬迁。
/// </summary>
public sealed class BaiduSpeechSynthesizer : ISpeechSynthesizer
{
    private const string Cuid = "geek_ocr_v4"; // v4 独立 cuid
    private readonly HttpClient _client;
    private readonly IBaiduTokenProvider _tokens;

    public BaiduSpeechSynthesizer(IBaiduTokenProvider tokens, HttpClient client)
    {
        _tokens = tokens;
        _client = client;
    }

    public async Task<byte[]> SynthesizeAsync(string text, VoiceInfo voice, string lan, bool useWebSocket, CancellationToken ct = default)
    {
        if (useWebSocket)
            return await TextToSpeechWebSocketAsync(text, voice.Id, lan, ct);
        return await TextToSpeechAsync(text, voice.Id, lan, ct);
    }

    /// <summary>HTTP 协议合成。</summary>
    public async Task<byte[]> TextToSpeechAsync(string text, int per, string lan, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();
        string token = await _tokens.GetVoiceTokenAsync(cancellationToken);
        string url = "https://tsn.baidu.com/text2audio";
        var fields = new Dictionary<string, string>
        {
            { "tex", text }, { "tok", token }, { "cuid", Cuid }, { "ctp", "1" },
            { "lan", lan }, { "per", per.ToString() }, { "spd", "5" }, { "pit", "5" }, { "vol", "7" }
        };
        var content = new FormUrlEncodedContent(fields);
        var response = await _client.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType != null && contentType.Contains("json"))
        {
            string jsonStr = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.TryGetProperty("err_msg", out var errMsg)) throw new Exception($"语音合成失败: {errMsg.GetString()}");
            throw new Exception("语音合成未知错误");
        }
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// WebSocket TTS 引擎：遵照四步状态机。使用 ArrayPool 内存池防止 GC 内存爆炸。
    /// </summary>
    public async Task<byte[]> TextToSpeechWebSocketAsync(string text, int per, string lan, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();
        string token = await _tokens.GetVoiceTokenAsync(cancellationToken);

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
        ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0");

        string url = $"wss://aip.baidubce.com/ws/2.0/speech/publiccloudspeech/v1/tts?access_token={token}&per={per}";
        await ws.ConnectAsync(new Uri(url), cancellationToken);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(16384);
        using var ms = new MemoryStream();

        try
        {
            var startPayload = new { type = "system.start", payload = new { spd = 5, pit = 5, vol = 7, aue = 3 } };
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(startPayload))), WebSocketMessageType.Text, true, cancellationToken);

            var startResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            string startResponse = Encoding.UTF8.GetString(buffer, 0, startResult.Count);
            if (startResponse.Contains("\"code\":") && !startResponse.Contains("\"code\":0")) throw new Exception($"Start 握手失败: {startResponse}");

            var textPayload = new { type = "text", payload = new { text = text } };
            await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(textPayload))), WebSocketMessageType.Text, true, cancellationToken);

            while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Binary) ms.Write(buffer, 0, result.Count);
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (msg.Contains("system.error") || (msg.Contains("\"code\"") && !msg.Contains("\"code\":0"))) throw new Exception($"百度服务端异常: {msg}");
                    break;
                }
                else if (result.MessageType == WebSocketMessageType.Close) break;
            }

            if (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var finishPayload = new { type = "system.finish" };
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(finishPayload))), WebSocketMessageType.Text, true, cancellationToken);
                try { await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken); } catch { }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            }
            catch { }
            ws.Dispose();
        }
        return ms.ToArray();
    }
}
