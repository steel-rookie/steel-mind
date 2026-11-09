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
        static string key, model;
        static bool loaded;

        public static bool Available { get { Load(); return !string.IsNullOrEmpty(key); } }
        public static string Model { get { Load(); return model; } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            key = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            model = Environment.GetEnvironmentVariable("GEMINI_MODEL");
            foreach (var rel in new[] { "../.env", "../../../.env", "../../.env" })
            {
                try
                {
                    var p = Path.GetFullPath(Path.Combine(Application.dataPath, rel));
                    if (!File.Exists(p)) continue;
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
            if (string.IsNullOrEmpty(model)) model = "gemini-2.5-flash";
        }

        [Serializable] class Part { public string text; }
        [Serializable] class Content { public Part[] parts; }
        [Serializable] class Candidate { public Content content; }
        [Serializable] class Response { public Candidate[] candidates; }

        /// <summary>prompt를 보내고 JSON 문자열을 done(text)로 돌려준다. 실패하면 done(null).</summary>
        public static IEnumerator Ask(string prompt, float timeout, Action<string> done)
        {
            if (!Available) { done(null); yield break; }
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
                    done(null); yield break;
                }
                try
                {
                    var r = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                    done(r?.candidates?[0]?.content?.parts?[0]?.text);
                }
                catch (Exception e) { Debug.LogWarning("[Gemini] 응답 해석 실패: " + e.Message); done(null); }
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
