using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SteelMind.Scenario
{
    /// <summary>
    /// Gemini 호출 (JSON 응답). 키는 저장소 루트의 .env(GEMINI_API_KEY)나 환경 변수에서만 읽는다.
    /// 키가 없거나 시간 안에 답이 없으면 null을 돌려주고, 게임은 규칙 기반 문장으로 계속한다.
    /// </summary>
    public static class GeminiClient
    {
        static string key, model, found;
        // 빠르고 안정적인 기본 모델 (2026-11 테스트: 응답 약 1초). gemini-2.5-flash는 새 사용자에게 막혀 404
        const string Fallback = "gemini-3.5-flash-lite";
        static bool loaded;

        public static bool Available { get { Load(); return !string.IsNullOrEmpty(key); } }
        public static string Model { get { Load(); return model; } }

        static void Load()
        {
            // 키를 못 찾았으면 매번 다시 읽는다 (.env를 나중에 저장해도 다시 Play하면 바로 연결)
            if (loaded && !string.IsNullOrEmpty(key)) return;
            loaded = true;
            key = null; model = null;
            key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            model = Environment.GetEnvironmentVariable("GEMINI_MODEL");
            foreach (var rel in new[] { "../.env", "../../../.env", "../../.env" })
            {
                try
                {
                    var p = Path.GetFullPath(Path.Combine(Application.dataPath, rel));
                    if (!File.Exists(p)) continue;
                    found = p;
                    foreach (var raw in File.ReadAllLines(p))
                    {
                        var line = raw.Trim();
                        if (line.StartsWith("#") || !line.Contains("=")) continue;
                        int i = line.IndexOf('=');
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim().Trim('"');
                        if (k == "GEMINI_API_KEY" && string.IsNullOrEmpty(key)) key = v;
                        if (k == "GEMINI_MODEL" && string.IsNullOrEmpty(model)) model = v;
                    }
                }
                catch { }
            }
            if (string.IsNullOrEmpty(model)) model = Fallback;
            Debug.Log(string.IsNullOrEmpty(key)
                ? $"[Gemini] 키 없음 → 규칙 기반으로 진행 (.env 위치: {found ?? "못 찾음"}, GEMINI_API_KEY=값 이 있어야 함)"
                : $"[Gemini] 연결 준비됨 · 모델 {model}");
        }

        [Serializable] class Part { public string text; }
        [Serializable] class Content { public Part[] parts; }
        [Serializable] class Candidate { public Content content; }
        [Serializable] class Response { public Candidate[] candidates; }

        /// <summary>prompt를 보내고 JSON 문자열을 done(text)로 돌려준다. 실패하면 done(null).</summary>
        public static IEnumerator Ask(string prompt, float timeout, Action<string> done)
        {
            if (!Available) { done(null); yield break; }
            string result = null; long code = 0;
            yield return Send(prompt, timeout, (t, c) => { result = t; code = c; });
            if (result == null && code == 404 && model != Fallback)
            {
                Debug.LogWarning($"[Gemini] 모델 {model} 을(를) 쓸 수 없어 {Fallback} 로 다시 시도합니다");
                model = Fallback;
                yield return Send(prompt, timeout, (t, c) => { result = t; code = c; });
            }
            done(result);
        }

        static IEnumerator Send(string prompt, float timeout, Action<string, long> done)
        {
            string body = "{\"contents\":[{\"parts\":[{\"text\":" + Quote(prompt) + "}]}]," +
                          "\"generationConfig\":{\"responseMimeType\":\"application/json\",\"temperature\":0.4}}";
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("x-goog-api-key", key);
                req.timeout = Mathf.CeilToInt(timeout);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[Gemini] 실패: {req.responseCode} {req.error}");
                    done(null, req.responseCode); yield break;
                }
                try
                {
                    var r = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                    var parts = r?.candidates?[0]?.content?.parts;
                    done(parts != null && parts.Length > 0 ? parts[parts.Length - 1].text : null, req.responseCode);
                }
                catch (Exception e) { Debug.LogWarning("[Gemini] 응답 해석 실패: " + e.Message); done(null, req.responseCode); }
            }
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 0x20) sb.Append($"\\u{(int)c:x4}"); else sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
