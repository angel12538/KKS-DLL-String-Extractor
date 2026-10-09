namespace KksDllStringExtractor;

internal record RawRow(string Dll, string Kind, string Source, string Method, string Offset, string Text, string Usage = "Unknown", string CallTarget = "");
internal record Report(List<RawRow> Rows, List<string> Errors, int DllCount, string Mode);
internal record Analyzed(RawRow Raw, string Script, string LanguageHint, string Category, string UiConfidence, int Score, string Reason, bool IsCandidate, bool IsUi);
