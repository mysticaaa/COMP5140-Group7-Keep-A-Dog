using System;
using System.Collections.Generic;
using System.Text;

namespace KeepADog.Demo
{
    /// <summary>Optional metadata on the canonical action plan; never grants execution or rewards.</summary>
    [Serializable]
    public sealed class DogTrainingDirective
    {
        public string mode = "none";
        public DogAgentAction alternative;
        public string failureReply;
        public bool sincere;

        internal static bool TryRead(string json, out DogTrainingDirective directive)
        {
            directive = null;
            if (!JsonReader.TryRead(json, out JsonValue root) || root.Kind != JsonKind.Object) return false;
            if (!root.Members.TryGetValue("training", out JsonValue training) || training.Kind == JsonKind.Null)
                return true;
            if (training.Kind != JsonKind.Object || !String(training, "mode", out string mode)) return false;
            if (mode == "none")
            {
                if (training.Members.Count != 1) return false;
                directive = new DogTrainingDirective();
                return true;
            }
            if (mode == "praise")
            {
                if (training.Members.Count != 2 || !training.Members.TryGetValue("sincere", out JsonValue sincere) ||
                    sincere.Kind != JsonKind.Boolean) return false;
                directive = new DogTrainingDirective { mode = mode, sincere = sincere.Boolean };
                return true;
            }
            if (mode != "practice" || training.Members.Count != 3 ||
                !training.Members.TryGetValue("alternative", out JsonValue alternative) ||
                alternative.Kind != JsonKind.Object || alternative.Members.Count != 2 ||
                !String(alternative, "id", out string id) || !String(alternative, "target", out string target) ||
                !String(training, "failureReply", out string failureReply) ||
                string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target)) return false;
            failureReply = failureReply.Replace("<", "").Replace(">", "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (failureReply.Length == 0 || failureReply.Length > 100) return false;
            directive = new DogTrainingDirective
            {
                mode = mode,
                alternative = new DogAgentAction { id = id, target = target },
                failureReply = failureReply
            };
            return true;
        }

        private static bool String(JsonValue value, string key, out string text)
        {
            text = null;
            if (!value.Members.TryGetValue(key, out JsonValue field) || field.Kind != JsonKind.String) return false;
            text = field.Text;
            return true;
        }

        private enum JsonKind { Object, Array, String, Boolean, Number, Null }

        private sealed class JsonValue
        {
            public JsonKind Kind;
            public string Text;
            public bool Boolean;
            public Dictionary<string, JsonValue> Members;
        }

        // Read raw token types because JsonUtility can coerce null strings and primitive fields.
        // Bounded by the controller envelope and a depth limit; duplicate keys fail closed.
        private sealed class JsonReader
        {
            private readonly string json;
            private int index;

            private JsonReader(string json) { this.json = json; }

            public static bool TryRead(string json, out JsonValue value)
            {
                value = null;
                if (string.IsNullOrWhiteSpace(json) || json.Length > 1024) return false;
                var reader = new JsonReader(json);
                if (!reader.ReadValue(0, out value)) return false;
                reader.SkipSpace();
                return reader.index == json.Length;
            }

            private bool ReadValue(int depth, out JsonValue value)
            {
                value = null;
                SkipSpace();
                if (depth > 16 || index >= json.Length) return false;
                char token = json[index];
                if (token == '{') return ReadObject(depth, out value);
                if (token == '[') return ReadArray(depth, out value);
                if (token == '"')
                {
                    if (!ReadString(out string text)) return false;
                    value = new JsonValue { Kind = JsonKind.String, Text = text };
                    return true;
                }
                if (Consume("true")) { value = new JsonValue { Kind = JsonKind.Boolean, Boolean = true }; return true; }
                if (Consume("false")) { value = new JsonValue { Kind = JsonKind.Boolean, Boolean = false }; return true; }
                if (Consume("null")) { value = new JsonValue { Kind = JsonKind.Null }; return true; }
                if (!ReadNumber()) return false;
                value = new JsonValue { Kind = JsonKind.Number };
                return true;
            }

            private bool ReadObject(int depth, out JsonValue value)
            {
                value = new JsonValue { Kind = JsonKind.Object, Members = new Dictionary<string, JsonValue>(StringComparer.Ordinal) };
                index++;
                SkipSpace();
                if (Take('}')) return true;
                while (index < json.Length)
                {
                    if (!ReadString(out string key) || value.Members.ContainsKey(key)) return false;
                    SkipSpace();
                    if (!Take(':') || !ReadValue(depth + 1, out JsonValue member)) return false;
                    value.Members.Add(key, member);
                    SkipSpace();
                    if (Take('}')) return true;
                    if (!Take(',')) return false;
                    SkipSpace();
                }
                return false;
            }

            private bool ReadArray(int depth, out JsonValue value)
            {
                value = new JsonValue { Kind = JsonKind.Array };
                index++;
                SkipSpace();
                if (Take(']')) return true;
                while (index < json.Length)
                {
                    if (!ReadValue(depth + 1, out _)) return false;
                    SkipSpace();
                    if (Take(']')) return true;
                    if (!Take(',')) return false;
                }
                return false;
            }

            private bool ReadString(out string value)
            {
                value = null;
                if (!Take('"')) return false;
                var text = new StringBuilder();
                while (index < json.Length)
                {
                    char letter = json[index++];
                    if (letter == '"') { value = text.ToString(); return true; }
                    if (letter < 0x20) return false;
                    if (letter != '\\') { text.Append(letter); continue; }
                    if (index >= json.Length) return false;
                    switch (json[index++])
                    {
                        case '"': text.Append('"'); break;
                        case '\\': text.Append('\\'); break;
                        case '/': text.Append('/'); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            if (index + 4 > json.Length) return false;
                            int code = 0;
                            for (int digit = 0; digit < 4; digit++)
                            {
                                char hex = json[index++];
                                int number = hex >= '0' && hex <= '9' ? hex - '0' :
                                    hex >= 'a' && hex <= 'f' ? hex - 'a' + 10 : hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
                                if (number < 0) return false;
                                code = code * 16 + number;
                            }
                            text.Append((char)code);
                            break;
                        default: return false;
                    }
                }
                return false;
            }

            private bool ReadNumber()
            {
                Take('-');
                if (index >= json.Length) return false;
                if (!Take('0'))
                {
                    if (json[index] < '1' || json[index] > '9') return false;
                    while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++;
                }
                if (Take('.'))
                {
                    int start = index;
                    while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++;
                    if (start == index) return false;
                }
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    int start = index;
                    while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++;
                    if (start == index) return false;
                }
                return true;
            }

            private bool Consume(string literal)
            {
                if (index + literal.Length > json.Length || string.CompareOrdinal(json, index, literal, 0, literal.Length) != 0)
                    return false;
                index += literal.Length;
                return true;
            }

            private bool Take(char letter)
            {
                if (index >= json.Length || json[index] != letter) return false;
                index++;
                return true;
            }

            private void SkipSpace()
            {
                while (index < json.Length && (json[index] == ' ' || json[index] == '\r' || json[index] == '\n' || json[index] == '\t')) index++;
            }
        }
    }
}
