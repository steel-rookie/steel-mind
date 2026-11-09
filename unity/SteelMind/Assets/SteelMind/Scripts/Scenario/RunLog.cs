using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SteelMind.Scenario
{
    /// <summary>
    /// 한 판의 진단 기록을 steelmind-run-v2 형식(schemas/steelmind-run-v2.json)으로 저장한다.
    /// 교육 담당자가 기록 폴더의 파일들을 tools/cohort-report 에 불러오면 기수 진단 보고서가 된다.
    /// 기록만 한다: 점수·사건 흐름·결과 계산은 바꾸지 않는다. 이름·사번은 넣지 않는다.
    /// </summary>
    public static class RunLog
    {
        public const string RoleKey = "steelmind_role";
        public static string Folder => Path.GetFullPath(Path.Combine(Application.persistentDataPath, "steelmind_runs"));

        /// <summary>보고서의 비교 기준 (신입 | 경력). 결과 화면에서 고르고, 다음 판에도 이어 쓴다.</summary>
        public static string Role
        {
            get { var r = PlayerPrefs.GetString(RoleKey, "신입"); return r == "경력" ? "경력" : "신입"; }
            set { PlayerPrefs.SetString(RoleKey, value == "경력" ? "경력" : "신입"); PlayerPrefs.Save(); }
        }

        public struct Entry { public ScenarioEvent ev; public int ci; public float sec, secChoice; public string kind; }

        /// <summary>기록을 쓰고 경로를 돌려준다. path 를 주면 그 파일을 덮어쓴다 (연차 구분을 바꿨을 때).</summary>
        public static string Save(ScenarioDef def, IList<Entry> entries, IEnumerable<(string key, string name, int score, int count)> comps, string path = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"format\": \"steelmind-run-v2\",\n");
            sb.Append($"  \"savedAt\": {S(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture))},\n");
            sb.Append("  \"source\": \"unity\",\n");
            sb.Append($"  \"scenario\": {S(def.id)},\n");
            sb.Append($"  \"role\": {S(Role)},\n");
            sb.Append("  \"virtual\": false,\n");
            var cs = comps.ToList();
            sb.Append("  \"competencies\": [").Append(string.Join(", ", cs.Select(c => $"{{\"key\": {S(c.key)}, \"name\": {S(c.name)}}}"))).Append("],\n");
            sb.Append("  \"scores\": {").Append(string.Join(", ", cs.Select(c => $"{S(c.key)}: {c.score}"))).Append("},\n");
            sb.Append("  \"counts\": {").Append(string.Join(", ", cs.Select(c => $"{S(c.key)}: {c.count}"))).Append("},\n");
            sb.Append("  \"log\": [\n");
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i]; var ev = e.ev; var c = ev.choices[e.ci];
                int bestCi = Array.FindIndex(ev.choices, x => x.best);
                var followed = ev.agents?.FirstOrDefault(a => a.rec == c.id)?.agent;
                sb.Append("    {\"type\": \"result\"");
                sb.Append($", \"id\": {S(ev.id)}, \"title\": {S(ev.title)}, \"kind\": {S(e.kind)}");
                sb.Append($", \"ci\": {e.ci}, \"choice\": {S(c.label)}, \"best\": {B(c.best)}, \"bestCi\": {bestCi}, \"accident\": {B(c.flag == "accident")}");
                sb.Append($", \"sec\": {F(e.sec)}, \"secChoice\": {F(e.secChoice)}, \"to\": false, \"pressure\": {B(ev.pressure)}");
                sb.Append(", \"probes\": [], \"report\": [], \"ci2\": null");
                sb.Append($", \"followedAgent\": {(followed == null ? "null" : S(followed))}");
                sb.Append(", \"tests\": [").Append(string.Join(", ", (ev.probes ?? new string[0]).Select(S))).Append("]}");
                sb.Append(i + 1 < entries.Count ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");

            Directory.CreateDirectory(Folder);
            path ??= Path.Combine(Folder, $"run_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[RunLog] 진단 기록 저장: " + path);
            return path;
        }

        /// <summary>기록 폴더를 탐색기로 연다 (교육 담당자가 파일을 모으기 쉽게).</summary>
        public static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            Application.OpenURL(new Uri(Folder).AbsoluteUri);
        }

        static string B(bool v) => v ? "true" : "false";
        static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        static string S(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (ch < 0x20) sb.Append($"\\u{(int)ch:x4}"); else sb.Append(ch); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
